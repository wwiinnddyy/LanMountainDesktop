using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    private const string DesktopComponentContentHostTag = "desktop-component-content-host";
    private const string DesktopComponentResizeHandleTag = "desktop-component-resize-handle";

    private bool _isDesktopComponentDragActive;
    private DesktopComponentDragState? _desktopComponentDrag;
    private Border? _desktopComponentDragGhost;
    private bool _isDesktopComponentResizeActive;
    private DesktopComponentResizeState? _desktopComponentResize;

    private string? _componentLibraryActiveCategoryId;
    private int _componentLibraryCategoryIndex;
    private int _componentLibraryComponentIndex;
    private double _componentLibraryCategoryPageWidth;
    private double _componentLibraryComponentPageWidth;
    private TranslateTransform? _componentLibraryCategoryHostTransform;
    private TranslateTransform? _componentLibraryComponentHostTransform;
    private IReadOnlyList<ComponentLibraryCategory> _componentLibraryCategories = Array.Empty<ComponentLibraryCategory>();
    private IReadOnlyList<DesktopComponentRuntimeDescriptor> _componentLibraryActiveComponents = Array.Empty<DesktopComponentRuntimeDescriptor>();
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

    private sealed class DesktopComponentResizeState
    {
        public string PlacementId { get; init; } = string.Empty;
        public string ComponentId { get; init; } = string.Empty;
        public Border SourceHost { get; init; } = null!;
        public int StartWidthCells { get; init; }
        public int StartHeightCells { get; init; }
        public int MinWidthCells { get; init; }
        public int MinHeightCells { get; init; }
        public int MaxWidthCells { get; init; }
        public int MaxHeightCells { get; init; }
        public Point StartPointerInViewport { get; init; }
        public int CurrentWidthCells { get; set; }
        public int CurrentHeightCells { get; set; }
    }

    private sealed record ComponentLibraryCategory(
        string Id,
        Symbol Icon,
        string Title,
        IReadOnlyList<DesktopComponentRuntimeDescriptor> Components);

    private readonly record struct ComponentScaleRule(int WidthUnit, int HeightUnit, int MinScale);

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

    private void OnCloseComponentSettingsClick(object? sender, RoutedEventArgs e)
    {
        CloseComponentSettingsWindow();
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

        _clockDisplayFormat = snapshot.ClockDisplayFormat == "HourMinute"
            ? ClockDisplayFormat.HourMinute
            : ClockDisplayFormat.HourMinuteSecond;

        if (ClockWidget is not null)
        {
            ClockWidget.SetDisplayFormat(_clockDisplayFormat);
        }

        if (_clockDisplayFormat == ClockDisplayFormat.HourMinute)
        {
            if (ClockFormatHMRadio is not null)
            {
                ClockFormatHMRadio.IsChecked = true;
            }
        }
        else
        {
            if (ClockFormatHMSSRadio is not null)
            {
                ClockFormatHMSSRadio.IsChecked = true;
            }
        }
    }

    private void ApplyTopStatusComponentVisibility()
    {
        var showClock = _topStatusComponentIds.Contains(BuiltInComponentIds.Clock);

        if (ClockWidget is not null)
        {
            ClockWidget.IsVisible = showClock;
            if (showClock)
            {
                ClockWidget.SetDisplayFormat(_clockDisplayFormat);
                var columnSpan = _clockDisplayFormat == ClockDisplayFormat.HourMinute ? 2 : 3;
                Grid.SetColumnSpan(ClockWidget, columnSpan);
            }
        }

        if (WallpaperPreviewClockWidget is not null)
        {
            WallpaperPreviewClockWidget.IsVisible = showClock;
            if (showClock)
            {
                WallpaperPreviewClockWidget.SetDisplayFormat(_clockDisplayFormat);
            }
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
            4 => TaskbarContext.SettingsWeather,
            5 => TaskbarContext.SettingsRegion,
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
        var showDesktopEdit = _isSettingsOpen;

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
        BuildDynamicTaskbarVisuals(dynamicActions, _currentDesktopCellSize);

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
        RestoreComponentLibraryWindowPosition();

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
        CancelDesktopComponentResize(restoreOriginalSpan: true);
        ClearDesktopComponentSelection();
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
            ClearComponentLibraryPreviewControls();

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
            var actions = new List<TaskbarActionItem>();
            if (_selectedDesktopComponentHost is not null)
            {
                actions.Add(new TaskbarActionItem(
                    TaskbarActionId.DeleteComponent,
                    L("component.delete", "Delete"),
                    "Delete",
                    IsVisible: true,
                    CommandKey: "component.delete"));

                actions.Add(new TaskbarActionItem(
                    TaskbarActionId.EditComponent,
                    L("component.edit", "Edit"),
                    "Edit",
                    IsVisible: true,
                    CommandKey: "component.edit"));

                return actions;
            }

            var canAddPage = _desktopPageCount < MaxDesktopPageCount;
            var canDeletePage = _desktopPageCount > MinDesktopPageCount;

            if (canAddPage)
            {
                actions.Add(new TaskbarActionItem(
                    TaskbarActionId.AddDesktopPage,
                    L("desktop.add_page", "Add page"),
                    "Add",
                    IsVisible: true,
                    CommandKey: "desktop.add_page"));
            }

            if (canDeletePage)
            {
                actions.Add(new TaskbarActionItem(
                    TaskbarActionId.DeleteDesktopPage,
                    L("desktop.delete_page", "Delete page"),
                    "Delete",
                    IsVisible: true,
                    CommandKey: "desktop.delete_page"));
            }

            return actions;
        }

        if (!_enableDynamicTaskbarActions)
        {
            return Array.Empty<TaskbarActionItem>();
        }

        _ = context;
        return Array.Empty<TaskbarActionItem>();
    }

    private void BuildDynamicTaskbarVisuals(IReadOnlyList<TaskbarActionItem> actions, double cellSize)
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

        // Match taskbar typographic scale to the current grid cell size.
        var taskbarCellHeight = Math.Clamp(cellSize * 0.76, 36, 76);
        var fontSize = Math.Clamp(taskbarCellHeight * 0.36, 11, 22);
        var iconSize = Math.Clamp(taskbarCellHeight * 0.44, 12, 26);
        var padding = Math.Clamp(taskbarCellHeight * 0.20, 6, 14);
        var cornerRadius = Math.Clamp(taskbarCellHeight * 0.32, 8, 16);
        var spacing = Math.Clamp(taskbarCellHeight * 0.18, 4, 10);

        var pageCountText = $"{_currentDesktopSurfaceIndex + 1}/{_desktopPageCount}";
        var pageCountBlock = new TextBlock
        {
            Text = pageCountText,
            Foreground = GetThemeBrush("AdaptiveTextSecondaryBrush"),
            FontSize = fontSize,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, spacing, 0)
        };

        var pageCountContainer = new Border
        {
            Background = GetThemeBrush("AdaptiveButtonBackgroundBrush"),
            CornerRadius = new CornerRadius(cornerRadius),
            Padding = new Thickness(padding),
            Child = pageCountBlock,
            Margin = new Thickness(0, 0, spacing, 0)
        };

        TaskbarDynamicActionsPanel.Children.Add(pageCountContainer);

        foreach (var action in actions)
        {
            if (!action.IsVisible)
            {
                continue;
            }

            var isDeleteAction = action.Id == TaskbarActionId.DeleteDesktopPage ||
                                 action.Id == TaskbarActionId.DeleteComponent;
            var isEditAction = action.Id == TaskbarActionId.EditComponent;

            Symbol iconSymbol;
            if (isDeleteAction)
            {
                iconSymbol = Symbol.Delete;
            }
            else if (isEditAction)
            {
                iconSymbol = Symbol.Edit;
            }
            else
            {
                iconSymbol = Symbol.Add;
            }

            Control icon = new SymbolIcon
            {
                Symbol = iconSymbol,
                IconVariant = IconVariant.Regular,
                FontSize = iconSize
            };

            var buttonContent = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = spacing * 0.6,
                Children =
                {
                    icon,
                    new TextBlock
                    {
                        Text = action.Title,
                        FontSize = fontSize,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    }
                }
            };

            var button = new Button
            {
                Content = buttonContent,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(padding),
                Foreground = isDeleteAction
                    ? new SolidColorBrush(Color.Parse("#FFFF6B6B"))
                    : Foreground,
                Tag = action.CommandKey
            };
            button.Click += OnDynamicTaskbarActionClick;

            TaskbarDynamicActionsPanel.Children.Add(button);

            Control previewIcon = new SymbolIcon
            {
                Symbol = iconSymbol,
                IconVariant = IconVariant.Regular,
                FontSize = iconSize * 0.85
            };

            var previewText = new TextBlock
            {
                Text = action.Title,
                FontSize = fontSize * 0.85,
                Foreground = isDeleteAction
                    ? new SolidColorBrush(Color.Parse("#FFFF6B6B"))
                    : Foreground,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            var previewContent = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = spacing * 0.5,
                Children = { previewIcon, previewText }
            };

            var previewBorder = new Border
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Child = previewContent
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
            case "desktop.delete_page":
                DeleteCurrentDesktopPage();
                break;
            case "component.delete":
                DeleteSelectedComponent();
                break;
            case "component.edit":
                OpenComponentSettings();
                break;
        }
    }

    private void DeleteSelectedComponent()
    {
        if (_selectedDesktopComponentHost is null || _selectedDesktopComponentHost.Tag is not string placementId)
        {
            return;
        }

        var placement = _desktopComponentPlacements.FirstOrDefault(p =>
            string.Equals(p.PlacementId, placementId, StringComparison.OrdinalIgnoreCase));
        if (placement is null)
        {
            return;
        }

        ClearTimeZoneServiceBindings(_selectedDesktopComponentHost);

        if (_desktopPageComponentGrids.TryGetValue(placement.PageIndex, out var pageGrid))
        {
            pageGrid.Children.Remove(_selectedDesktopComponentHost);
        }

        // Remove from persisted placement list as well.
        _desktopComponentPlacements.Remove(placement);

        ClearDesktopComponentSelection();

        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());

        // 娣囨繂鐡ㄧ拋鍓х枂
        PersistSettings();
    }

    private void OpenComponentSettings()
    {
        if (_selectedDesktopComponentHost is null || _selectedDesktopComponentHost.Tag is not string placementId)
        {
            return;
        }

        var placement = _desktopComponentPlacements.FirstOrDefault(p =>
            string.Equals(p.PlacementId, placementId, StringComparison.OrdinalIgnoreCase));
        if (placement is null)
        {
            return;
        }

        if (placement.ComponentId == BuiltInComponentIds.Date)
        {
            OpenDateComponentSettings();
            return;
        }

        if (placement.ComponentId == BuiltInComponentIds.DesktopClassSchedule)
        {
            OpenClassScheduleComponentSettings();
        }
    }

    private void OpenDateComponentSettings()
    {
        if (ComponentSettingsWindow is null || ComponentSettingsContentHost is null)
        {
            return;
        }

        var settingsContent = new DateWidgetSettingsWindow();
        ComponentSettingsContentHost.Content = settingsContent;
        
        ComponentSettingsWindow.IsVisible = true;
        ComponentSettingsWindow.Opacity = 0;
        
        ComponentSettingsWindow.Opacity = 1;
    }

    private void OpenClassScheduleComponentSettings()
    {
        if (ComponentSettingsWindow is null || ComponentSettingsContentHost is null)
        {
            return;
        }

        var settingsContent = new ClassScheduleSettingsWindow();
        settingsContent.SettingsChanged += OnClassScheduleSettingsChanged;
        ComponentSettingsContentHost.Content = settingsContent;

        ComponentSettingsWindow.IsVisible = true;
        ComponentSettingsWindow.Opacity = 0;
        ComponentSettingsWindow.Opacity = 1;
    }

    private void OnClassScheduleSettingsChanged(object? sender, EventArgs e)
    {
        if (_selectedDesktopComponentHost is null)
        {
            return;
        }

        if (TryGetContentHost(_selectedDesktopComponentHost)?.Child is ClassScheduleWidget widget)
        {
            widget.RefreshFromSettings();
        }
    }

    private void CloseComponentSettingsWindow()
    {
        if (ComponentSettingsWindow is null)
        {
            return;
        }

        if (ComponentSettingsContentHost?.Content is ClassScheduleSettingsWindow classScheduleSettingsWindow)
        {
            classScheduleSettingsWindow.SettingsChanged -= OnClassScheduleSettingsChanged;
        }

        ComponentSettingsWindow.Opacity = 0;
        
        DispatcherTimer.RunOnce(() =>
        {
            if (ComponentSettingsWindow is not null)
            {
                ComponentSettingsWindow.IsVisible = false;
            }
            if (ComponentSettingsContentHost is not null)
            {
                ComponentSettingsContentHost.Content = null;
            }
        }, TimeSpan.FromMilliseconds(200));
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
        
        // Refresh taskbar actions after page count changes.
        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
    }

    private void DeleteCurrentDesktopPage()
    {
        if (_desktopPageCount <= MinDesktopPageCount)
        {
            return;
        }

        var placementsToRemove = _desktopComponentPlacements
            .Where(p => p.PageIndex == _currentDesktopSurfaceIndex)
            .ToList();

        if (_desktopPageComponentGrids.TryGetValue(_currentDesktopSurfaceIndex, out var pageGrid))
        {
            ClearTimeZoneServiceBindings(pageGrid.Children.OfType<Control>().ToList());
        }
        
        foreach (var placement in placementsToRemove)
        {
            _desktopComponentPlacements.Remove(placement);
        }

        _desktopPageCount = Math.Clamp(_desktopPageCount - 1, MinDesktopPageCount, MaxDesktopPageCount);
        
        // Clamp current page index to valid range after deletion.
        _currentDesktopSurfaceIndex = Math.Clamp(_currentDesktopSurfaceIndex, 0, _desktopPageCount - 1);
        
        // Update remaining page indices after deletion.
        foreach (var placement in _desktopComponentPlacements)
        {
            if (placement.PageIndex > _currentDesktopSurfaceIndex)
            {
                placement.PageIndex--;
            }
        }

        RebuildDesktopGrid();
        PersistSettings();
        
        // Refresh taskbar actions after page count changes.
        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
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
            if (!_componentRuntimeRegistry.TryGetDescriptor(componentId, out var runtimeDescriptor) ||
                !runtimeDescriptor.Definition.AllowDesktopPlacement)
            {
                continue;
            }

            var (widthCells, heightCells) = NormalizeComponentCellSpan(
                componentId,
                ComponentPlacementRules.EnsureMinimumSize(
                    runtimeDescriptor.Definition,
                    placement.WidthCells,
                    placement.HeightCells));

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

        ClearTimeZoneServiceBindings(pageGrid.Children.OfType<Control>().ToList());
        pageGrid.Children.Clear();

        var maxColumns = pageGrid.ColumnDefinitions.Count;
        var maxRows = pageGrid.RowDefinitions.Count;
        if (maxColumns <= 0 || maxRows <= 0)
        {
            return;
        }

        foreach (var placement in _desktopComponentPlacements.Where(p => p.PageIndex == pageIndex))
        {
            if (!_componentRuntimeRegistry.TryGetDescriptor(placement.ComponentId, out var runtimeDescriptor) ||
                !runtimeDescriptor.Definition.AllowDesktopPlacement)
            {
                continue;
            }

            var (widthCells, heightCells) = NormalizeComponentCellSpan(
                placement.ComponentId,
                ComponentPlacementRules.EnsureMinimumSize(
                    runtimeDescriptor.Definition,
                    placement.WidthCells,
                    placement.HeightCells));

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

        if (!_componentRuntimeRegistry.TryGetDescriptor(componentId, out var runtimeDescriptor) ||
            !runtimeDescriptor.Definition.AllowDesktopPlacement)
        {
            return;
        }

        var (widthCells, heightCells) = NormalizeComponentCellSpan(
            componentId,
            ComponentPlacementRules.EnsureMinimumSize(
                runtimeDescriptor.Definition,
                runtimeDescriptor.Definition.MinWidthCells,
                runtimeDescriptor.Definition.MinHeightCells));

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

        var componentCornerRadius = GetComponentCornerRadius(placement.ComponentId);

        var visualInset = GetDesktopComponentVisualInset(
            Math.Max(1, placement.WidthCells),
            Math.Max(1, placement.HeightCells));

        var contentHost = new Border
        {
            Tag = DesktopComponentContentHostTag,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(componentCornerRadius),
            ClipToBounds = true,
            Padding = visualInset,
            Child = component
        };

        // Separate visual arc size from hit target size for better touch usability.
        var handleTouchSize = Math.Clamp(_currentDesktopCellSize * 0.72, 30, 54);
        var handleVisualSize = Math.Clamp(_currentDesktopCellSize * 0.56, 20, 40);
        var handlePadding = Math.Max(2, (handleTouchSize - handleVisualSize) / 2);
        var arcThickness = Math.Clamp(_currentDesktopCellSize * 0.17, 7, 14);
        var arcData = Geometry.Parse("M 24,6 A 18,18 0 0 1 6,24");

        var resizeHandleVisual = new Grid
        {
            Width = handleVisualSize,
            Height = handleVisualSize,
            IsHitTestVisible = false
        };
        resizeHandleVisual.Children.Add(new Path
        {
            Data = arcData,
            Stretch = Stretch.Fill,
            Stroke = GetThemeBrush("AdaptiveTextAccentBrush"),
            StrokeThickness = arcThickness + 3,
            StrokeLineCap = PenLineCap.Round
        });
        resizeHandleVisual.Children.Add(new Path
        {
            Data = arcData,
            Stretch = Stretch.Fill,
            Stroke = GetThemeBrush("AdaptiveAccentBrush"),
            StrokeThickness = arcThickness,
            StrokeLineCap = PenLineCap.Round
        });

        var resizeHandle = new Border
        {
            Tag = DesktopComponentResizeHandleTag,
            Width = handleTouchSize,
            Height = handleTouchSize,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(handleTouchSize * 0.5),
            Padding = new Thickness(handlePadding),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(
                0,
                0,
                -Math.Clamp(handleTouchSize * 0.42, 10, 24),
                -Math.Clamp(handleTouchSize * 0.42, 10, 24)),
            Child = resizeHandleVisual,
            Opacity = 1,
            IsVisible = false,
            IsHitTestVisible = false
        };
        resizeHandle.PointerPressed += OnDesktopComponentResizeHandlePointerPressed;

        var hostChrome = new Grid
        {
            ClipToBounds = false
        };
        hostChrome.Children.Add(contentHost);
        hostChrome.Children.Add(resizeHandle);

        var host = new Border
        {
            Tag = placement.PlacementId,
            Background = Brushes.Transparent,
            ClipToBounds = false,
            CornerRadius = new CornerRadius(componentCornerRadius),
            Child = hostChrome
        };
        host.Classes.Add(DesktopComponentHostClass);
        ApplyDesktopEditStateToHost(host, _isComponentLibraryOpen);
        host.PointerPressed += OnDesktopComponentHostPointerPressed;
        return host;
    }

    private (int WidthCells, int HeightCells) NormalizeComponentCellSpan(
        string componentId,
        (int WidthCells, int HeightCells) span)
    {
        if (_componentRuntimeRegistry.TryGetDescriptor(componentId, out var runtimeDescriptor))
        {
            var normalized = ComponentPlacementRules.EnsureMinimumSize(
                runtimeDescriptor.Definition,
                span.WidthCells,
                span.HeightCells);
            if (runtimeDescriptor.Definition.ResizeMode == DesktopComponentResizeMode.Free)
            {
                return normalized;
            }

            return NormalizeAspectRatioForComponent(componentId, normalized);
        }

        return NormalizeAspectRatioForComponent(
            componentId,
            (Math.Max(1, span.WidthCells), Math.Max(1, span.HeightCells)));
    }

    private DesktopComponentResizeMode GetComponentResizeMode(string componentId)
    {
        if (_componentRuntimeRegistry.TryGetDescriptor(componentId, out var runtimeDescriptor))
        {
            return runtimeDescriptor.Definition.ResizeMode;
        }

        return DesktopComponentResizeMode.Proportional;
    }

    private static (int WidthCells, int HeightCells) NormalizeAspectRatioForComponent(
        string componentId,
        (int WidthCells, int HeightCells) span)
    {
        if (string.Equals(componentId, BuiltInComponentIds.DesktopWhiteboard, StringComparison.OrdinalIgnoreCase))
        {
            // Support both portrait ratios and snap to nearest viable scale tier.
            return SnapSpanToScaleRules(
                span,
                new ComponentScaleRule(WidthUnit: 1, HeightUnit: 2, MinScale: 2), // 2x4, 3x6, 4x8...
                new ComponentScaleRule(WidthUnit: 3, HeightUnit: 4, MinScale: 1)); // 3x4, 6x8...
        }

        if (string.Equals(componentId, BuiltInComponentIds.DesktopBlackboardLandscape, StringComparison.OrdinalIgnoreCase))
        {
            // Support both landscape ratios and snap to nearest viable scale tier.
            return SnapSpanToScaleRules(
                span,
                new ComponentScaleRule(WidthUnit: 2, HeightUnit: 1, MinScale: 2), // 4x2, 6x3, 8x4...
                new ComponentScaleRule(WidthUnit: 4, HeightUnit: 3, MinScale: 1)); // 4x3, 8x6...
        }

        if (string.Equals(componentId, BuiltInComponentIds.DesktopDailyPoetry, StringComparison.OrdinalIgnoreCase))
        {
            // Keep recommendation card at a 2:1 ratio with a minimum footprint of 4x2.
            return SnapSpanToScaleRules(
                span,
                new ComponentScaleRule(WidthUnit: 2, HeightUnit: 1, MinScale: 2));
        }

        return span;
    }

    private static (int WidthCells, int HeightCells) SnapSpanToScaleRules(
        (int WidthCells, int HeightCells) span,
        params ComponentScaleRule[] rules)
    {
        var targetWidth = Math.Max(1, span.WidthCells);
        var targetHeight = Math.Max(1, span.HeightCells);

        var hasCandidate = false;
        var bestWidth = targetWidth;
        var bestHeight = targetHeight;
        var bestArea = -1;
        var bestDistance = double.MaxValue;

        foreach (var rule in rules)
        {
            if (rule.WidthUnit <= 0 || rule.HeightUnit <= 0 || rule.MinScale <= 0)
            {
                continue;
            }

            var maxScale = Math.Min(targetWidth / rule.WidthUnit, targetHeight / rule.HeightUnit);
            if (maxScale < rule.MinScale)
            {
                continue;
            }

            for (var scale = rule.MinScale; scale <= maxScale; scale++)
            {
                var width = rule.WidthUnit * scale;
                var height = rule.HeightUnit * scale;
                var area = width * height;
                var dx = targetWidth - width;
                var dy = targetHeight - height;
                var distance = dx * dx + dy * dy;

                if (!hasCandidate ||
                    area > bestArea ||
                    (area == bestArea && distance < bestDistance))
                {
                    hasCandidate = true;
                    bestWidth = width;
                    bestHeight = height;
                    bestArea = area;
                    bestDistance = distance;
                }
            }
        }

        return hasCandidate
            ? (bestWidth, bestHeight)
            : (targetWidth, targetHeight);
    }

    private double GetComponentCornerRadius(string componentId)
    {
        if (_componentRuntimeRegistry.TryGetDescriptor(componentId, out var runtimeDescriptor))
        {
            return runtimeDescriptor.ResolveCornerRadius(_currentDesktopCellSize);
        }

        return Math.Clamp(_currentDesktopCellSize * 0.22, 8, 18);
    }

    private Thickness GetDesktopComponentVisualInset(int widthCells, int heightCells)
    {
        // Keep the drop/selection bounds on grid cells while reducing visual footprint.
        var baseInset = Math.Clamp(_currentDesktopCellSize * 0.08, 2, 10);
        var horizontal = Math.Clamp(baseInset + Math.Max(0, widthCells - 1) * 0.25, 2, 12);
        var vertical = Math.Clamp(baseInset * 0.85 + Math.Max(0, heightCells - 1) * 0.2, 2, 10);
        return new Thickness(horizontal, vertical, horizontal, vertical);
    }

    private static Border? FindDesktopComponentHost(Visual? visual)
    {
        var current = visual;
        while (current is not null)
        {
            if (current is Border border && border.Classes.Contains(DesktopComponentHostClass))
            {
                return border;
            }

            current = current.GetVisualParent();
        }

        return null;
    }

    private static Border? TryGetContentHost(Border host)
    {
        if (host.Child is Grid hostChrome)
        {
            return hostChrome.Children
                .OfType<Border>()
                .FirstOrDefault(child =>
                    string.Equals(child.Tag?.ToString(), DesktopComponentContentHostTag, StringComparison.Ordinal));
        }

        return null;
    }

    private static void ClearTimeZoneServiceBindings(IEnumerable<Control> roots)
    {
        foreach (var root in roots)
        {
            ClearTimeZoneServiceBindings(root);
        }
    }

    private static void ClearTimeZoneServiceBindings(Control root)
    {
        if (root is ITimeZoneAwareComponentWidget timeZoneAwareRoot)
        {
            timeZoneAwareRoot.ClearTimeZoneService();
        }

        foreach (var descendant in root.GetVisualDescendants())
        {
            if (descendant is ITimeZoneAwareComponentWidget timeZoneAwareChild)
            {
                timeZoneAwareChild.ClearTimeZoneService();
            }
        }
    }

    private static Border? TryGetResizeHandle(Border host)
    {
        if (host.Child is Grid hostChrome)
        {
            return hostChrome.Children
                .OfType<Border>()
                .FirstOrDefault(child =>
                    string.Equals(child.Tag?.ToString(), DesktopComponentResizeHandleTag, StringComparison.Ordinal));
        }

        return null;
    }

    private bool IsPointerOnSelectedFrameBorder(Border host, Point pointerInHost)
    {
        if (host != _selectedDesktopComponentHost || !_isComponentLibraryOpen)
        {
            return false;
        }

        var width = host.Bounds.Width;
        var height = host.Bounds.Height;
        if (width <= 1 || height <= 1)
        {
            return false;
        }

        var borderBand = Math.Clamp(_currentDesktopCellSize * 0.15, 8, 22);
        var onLeft = pointerInHost.X <= borderBand;
        var onRight = pointerInHost.X >= width - borderBand;
        var onTop = pointerInHost.Y <= borderBand;
        var onBottom = pointerInHost.Y >= height - borderBand;
        return onLeft || onRight || onTop || onBottom;
    }

    private Control? CreateDesktopComponentControl(string componentId)
    {
        if (!_componentRuntimeRegistry.TryGetDescriptor(componentId, out var runtimeDescriptor))
        {
            return null;
        }

        var component = runtimeDescriptor.CreateControl(_currentDesktopCellSize, _timeZoneService, _weatherDataService);
        component.Classes.Add(DesktopComponentClass);
        return component;
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
        CancelDesktopComponentResize(restoreOriginalSpan: true);
        ClearDesktopComponentSelection();
        UpdateDesktopComponentHostEditState();
        ClearComponentLibraryPreviewControls();
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
        host.IsHitTestVisible = true;

        if (TryGetContentHost(host) is Border contentHost)
        {
            // In edit mode, prefer drag interactions over component interactions.
            contentHost.IsHitTestVisible = !isEditMode;
            if (contentHost.Child is Control componentControl)
            {
                componentControl.IsHitTestVisible = !isEditMode;
            }
        }

        var isSelected = host == _selectedDesktopComponentHost;
        ApplySelectionStateToHost(host, isSelected);
    }

    private void OnDesktopComponentHostPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isComponentLibraryOpen || _isDesktopComponentDragActive || _isDesktopComponentResizeActive)
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

        var wasSelected = host == _selectedDesktopComponentHost;
        SetSelectedDesktopComponent(host);
        if (!wasSelected)
        {
            e.Handled = true;
            return;
        }

        var pointerInHost = e.GetPosition(host);
        if (IsPointerOnSelectedFrameBorder(host, pointerInHost))
        {
            BeginDesktopComponentResizeDrag(host, placement, e);
            if (_isDesktopComponentResizeActive)
            {
                e.Handled = true;
            }

            return;
        }

        BeginDesktopComponentMoveDrag(host, placement, e);
        e.Handled = true;
    }

    private void SetSelectedDesktopComponent(Border? host)
    {
        // Clear previous selection
        if (_selectedDesktopComponentHost is not null && _selectedDesktopComponentHost != host)
        {
            ApplySelectionStateToHost(_selectedDesktopComponentHost, false);
        }

        // Set new selection
        _selectedDesktopComponentHost = host;
        if (host is not null)
        {
            ApplySelectionStateToHost(host, true);
        }

        // Refresh taskbar actions to show delete/edit buttons
        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
    }

    private void ApplySelectionStateToHost(Border host, bool isSelected)
    {
        var showSelection = isSelected && _isComponentLibraryOpen;
        host.BorderThickness = showSelection
            ? new Thickness(Math.Clamp(_currentDesktopCellSize * 0.04, 1, 3))
            : new Thickness(0);
        host.BorderBrush = showSelection ? GetThemeBrush("AdaptiveAccentBrush") : null;

        if (TryGetResizeHandle(host) is Border resizeHandle)
        {
            resizeHandle.IsVisible = showSelection;
            resizeHandle.IsHitTestVisible = showSelection;
        }
    }

    private void ClearDesktopComponentSelection()
    {
        if (_selectedDesktopComponentHost is not null)
        {
            ApplySelectionStateToHost(_selectedDesktopComponentHost, false);
            _selectedDesktopComponentHost = null;
        }
    }

    private void BeginDesktopComponentMoveDrag(Border sourceHost, DesktopComponentPlacementSnapshot placement, PointerPressedEventArgs e)
    {
        if (_isDesktopComponentResizeActive ||
            DesktopEditDragLayer is null ||
            DesktopPagesViewport is null ||
            _currentDesktopCellSize <= 0 ||
            !_componentRuntimeRegistry.TryGetDescriptor(placement.ComponentId, out var runtimeDescriptor))
        {
            return;
        }

        var (widthCells, heightCells) = NormalizeComponentCellSpan(
            placement.ComponentId,
            ComponentPlacementRules.EnsureMinimumSize(
                runtimeDescriptor.Definition,
                placement.WidthCells,
                placement.HeightCells));

        var pointerInViewport = e.GetPosition(DesktopPagesViewport);
        var pitch = CurrentDesktopPitch;
        var topLeft = new Point(placement.Column * pitch, placement.Row * pitch);
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
            _isDesktopComponentResizeActive ||
            DesktopEditDragLayer is null ||
            DesktopPagesViewport is null ||
            _currentDesktopCellSize <= 0 ||
            !_componentRuntimeRegistry.TryGetDescriptor(componentId, out var runtimeDescriptor) ||
            !runtimeDescriptor.Definition.AllowDesktopPlacement)
        {
            return;
        }

        var (widthCells, heightCells) = NormalizeComponentCellSpan(
            componentId,
            ComponentPlacementRules.EnsureMinimumSize(
                runtimeDescriptor.Definition,
                runtimeDescriptor.Definition.MinWidthCells,
                runtimeDescriptor.Definition.MinHeightCells));

        // Center the component under the pointer while dragging from the library.
        var ghostWidth = Math.Max(1, widthCells * _currentDesktopCellSize + Math.Max(0, widthCells - 1) * _currentDesktopCellGap);
        var ghostHeight = Math.Max(1, heightCells * _currentDesktopCellSize + Math.Max(0, heightCells - 1) * _currentDesktopCellGap);
        var pointerOffset = new Point(
            ghostWidth * 0.5,
            ghostHeight * 0.5);

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

        var ghostWidth = Math.Max(1, widthCells * _currentDesktopCellSize + Math.Max(0, widthCells - 1) * _currentDesktopCellGap);
        var ghostHeight = Math.Max(1, heightCells * _currentDesktopCellSize + Math.Max(0, heightCells - 1) * _currentDesktopCellGap);

        var ghostContent = CreateDesktopComponentControl(componentId);
        if (ghostContent is not null)
        {
            ghostContent.IsHitTestVisible = false;
        }

        var visualInset = GetDesktopComponentVisualInset(widthCells, heightCells);

        _desktopComponentDragGhost = new Border
        {
            Width = ghostWidth,
            Height = ghostHeight,
            CornerRadius = new CornerRadius(Math.Clamp(_currentDesktopCellSize * 0.45, 16, 36)),
            Background = new SolidColorBrush(Color.Parse("#331E40AF")),
            BorderBrush = GetThemeBrush("AdaptiveAccentBrush"),
            BorderThickness = new Thickness(Math.Clamp(_currentDesktopCellSize * 0.04, 1, 3)),
            Padding = visualInset,
            ClipToBounds = true,
            Child = ghostContent,
            Opacity = 0.92,
            IsHitTestVisible = false
        };

        DesktopEditDragLayer.Children.Add(_desktopComponentDragGhost);
    }

    private void OnDesktopComponentResizeHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isComponentLibraryOpen ||
            _isDesktopComponentDragActive ||
            _isDesktopComponentResizeActive ||
            DesktopPagesViewport is null ||
            sender is not Border handle ||
            !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var host = FindDesktopComponentHost(handle);
        if (host?.Tag is not string placementId)
        {
            return;
        }

        var placement = _desktopComponentPlacements.FirstOrDefault(p =>
            string.Equals(p.PlacementId, placementId, StringComparison.OrdinalIgnoreCase));
        if (placement is null)
        {
            return;
        }

        SetSelectedDesktopComponent(host);
        BeginDesktopComponentResizeDrag(host, placement, e);
        if (_isDesktopComponentResizeActive)
        {
            e.Handled = true;
        }
    }

    private void BeginDesktopComponentResizeDrag(
        Border sourceHost,
        DesktopComponentPlacementSnapshot placement,
        PointerPressedEventArgs e)
    {
        if (DesktopPagesViewport is null ||
            _currentDesktopCellSize <= 0 ||
            !_componentRuntimeRegistry.TryGetDescriptor(placement.ComponentId, out var runtimeDescriptor) ||
            !_desktopPageComponentGrids.TryGetValue(placement.PageIndex, out var pageGrid))
        {
            return;
        }

        var startSpan = NormalizeComponentCellSpan(
            placement.ComponentId,
            ComponentPlacementRules.EnsureMinimumSize(
                runtimeDescriptor.Definition,
                placement.WidthCells,
                placement.HeightCells));

        var minSpan = NormalizeComponentCellSpan(
            placement.ComponentId,
            ComponentPlacementRules.EnsureMinimumSize(
                runtimeDescriptor.Definition,
                runtimeDescriptor.Definition.MinWidthCells,
                runtimeDescriptor.Definition.MinHeightCells));

        var maxWidthCells = Math.Max(startSpan.WidthCells, pageGrid.ColumnDefinitions.Count - placement.Column);
        var maxHeightCells = Math.Max(startSpan.HeightCells, pageGrid.RowDefinitions.Count - placement.Row);
        if (maxWidthCells <= 0 || maxHeightCells <= 0)
        {
            return;
        }

        var pointerInViewport = e.GetPosition(DesktopPagesViewport);
        _desktopComponentResize = new DesktopComponentResizeState
        {
            PlacementId = placement.PlacementId,
            ComponentId = placement.ComponentId,
            SourceHost = sourceHost,
            StartWidthCells = startSpan.WidthCells,
            StartHeightCells = startSpan.HeightCells,
            MinWidthCells = Math.Max(1, Math.Min(minSpan.WidthCells, maxWidthCells)),
            MinHeightCells = Math.Max(1, Math.Min(minSpan.HeightCells, maxHeightCells)),
            MaxWidthCells = maxWidthCells,
            MaxHeightCells = maxHeightCells,
            StartPointerInViewport = pointerInViewport,
            CurrentWidthCells = startSpan.WidthCells,
            CurrentHeightCells = startSpan.HeightCells
        };

        _isDesktopComponentResizeActive = true;
        sourceHost.Opacity = 0.96;
        e.Pointer.Capture(this);
    }

    private void UpdateDesktopComponentResizeVisual(Point pointerInViewport)
    {
        if (_desktopComponentResize is null)
        {
            return;
        }

        var pitch = CurrentDesktopPitch;
        if (pitch <= 0 ||
            _desktopComponentResize.StartWidthCells <= 0 ||
            _desktopComponentResize.StartHeightCells <= 0)
        {
            return;
        }

        var deltaX = pointerInViewport.X - _desktopComponentResize.StartPointerInViewport.X;
        var deltaY = pointerInViewport.Y - _desktopComponentResize.StartPointerInViewport.Y;
        int widthCells;
        int heightCells;
        if (GetComponentResizeMode(_desktopComponentResize.ComponentId) == DesktopComponentResizeMode.Free)
        {
            widthCells = Math.Clamp(
                (int)Math.Round(_desktopComponentResize.StartWidthCells + deltaX / pitch),
                _desktopComponentResize.MinWidthCells,
                _desktopComponentResize.MaxWidthCells);
            heightCells = Math.Clamp(
                (int)Math.Round(_desktopComponentResize.StartHeightCells + deltaY / pitch),
                _desktopComponentResize.MinHeightCells,
                _desktopComponentResize.MaxHeightCells);
        }
        else
        {
            var widthScale = (_desktopComponentResize.StartWidthCells + deltaX / pitch) / _desktopComponentResize.StartWidthCells;
            var heightScale = (_desktopComponentResize.StartHeightCells + deltaY / pitch) / _desktopComponentResize.StartHeightCells;

            var proposedScale = Math.Max(widthScale, heightScale);
            var minScale = Math.Max(
                (double)_desktopComponentResize.MinWidthCells / _desktopComponentResize.StartWidthCells,
                (double)_desktopComponentResize.MinHeightCells / _desktopComponentResize.StartHeightCells);
            var maxScale = Math.Min(
                (double)_desktopComponentResize.MaxWidthCells / _desktopComponentResize.StartWidthCells,
                (double)_desktopComponentResize.MaxHeightCells / _desktopComponentResize.StartHeightCells);

            if (double.IsNaN(proposedScale) || double.IsInfinity(proposedScale))
            {
                proposedScale = minScale;
            }

            if (maxScale < minScale)
            {
                maxScale = minScale;
            }

            var scale = Math.Clamp(proposedScale, minScale, maxScale);
            widthCells = Math.Clamp(
                (int)Math.Round(_desktopComponentResize.StartWidthCells * scale),
                _desktopComponentResize.MinWidthCells,
                _desktopComponentResize.MaxWidthCells);
            heightCells = Math.Clamp(
                (int)Math.Round(_desktopComponentResize.StartHeightCells * scale),
                _desktopComponentResize.MinHeightCells,
                _desktopComponentResize.MaxHeightCells);
        }

        var normalized = NormalizeComponentCellSpan(_desktopComponentResize.ComponentId, (widthCells, heightCells));
        widthCells = Math.Clamp(normalized.WidthCells, _desktopComponentResize.MinWidthCells, _desktopComponentResize.MaxWidthCells);
        heightCells = Math.Clamp(normalized.HeightCells, _desktopComponentResize.MinHeightCells, _desktopComponentResize.MaxHeightCells);

        _desktopComponentResize.CurrentWidthCells = widthCells;
        _desktopComponentResize.CurrentHeightCells = heightCells;
        Grid.SetColumnSpan(_desktopComponentResize.SourceHost, widthCells);
        Grid.SetRowSpan(_desktopComponentResize.SourceHost, heightCells);
    }

    private bool TryCompleteDesktopComponentResize(Point pointerInViewport)
    {
        if (_desktopComponentResize is null)
        {
            return false;
        }

        UpdateDesktopComponentResizeVisual(pointerInViewport);

        var placement = _desktopComponentPlacements.FirstOrDefault(p =>
            string.Equals(p.PlacementId, _desktopComponentResize.PlacementId, StringComparison.OrdinalIgnoreCase));
        if (placement is null)
        {
            return false;
        }

        var widthCells = Math.Max(1, _desktopComponentResize.CurrentWidthCells);
        var heightCells = Math.Max(1, _desktopComponentResize.CurrentHeightCells);
        var changed = placement.WidthCells != widthCells || placement.HeightCells != heightCells;
        placement.WidthCells = widthCells;
        placement.HeightCells = heightCells;

        ApplyDesktopEditStateToHost(_desktopComponentResize.SourceHost, _isComponentLibraryOpen);
        if (changed)
        {
            PersistSettings();
        }

        return true;
    }

    private void CancelDesktopComponentResize(bool restoreOriginalSpan)
    {
        if (!_isDesktopComponentResizeActive || _desktopComponentResize is null)
        {
            return;
        }

        if (restoreOriginalSpan)
        {
            Grid.SetColumnSpan(_desktopComponentResize.SourceHost, _desktopComponentResize.StartWidthCells);
            Grid.SetRowSpan(_desktopComponentResize.SourceHost, _desktopComponentResize.StartHeightCells);
        }

        _desktopComponentResize.SourceHost.Opacity = 1;
        ApplyDesktopEditStateToHost(_desktopComponentResize.SourceHost, _isComponentLibraryOpen);
        _desktopComponentResize = null;
        _isDesktopComponentResizeActive = false;
    }

    private void OnDesktopComponentDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DesktopPagesViewport is null)
        {
            return;
        }

        if (_isDesktopComponentResizeActive && _desktopComponentResize is not null)
        {
            UpdateDesktopComponentResizeVisual(e.GetPosition(DesktopPagesViewport));
            return;
        }

        if (!_isDesktopComponentDragActive || _desktopComponentDrag is null)
        {
            return;
        }

        UpdateDesktopComponentDragVisual(e.GetPosition(DesktopPagesViewport));
    }

    private void OnDesktopComponentDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DesktopPagesViewport is null)
        {
            return;
        }

        if (_isDesktopComponentResizeActive && _desktopComponentResize is not null)
        {
            var resizePointerInViewport = e.GetPosition(DesktopPagesViewport);
            var resizeSuccess = TryCompleteDesktopComponentResize(resizePointerInViewport);
            CancelDesktopComponentResize(restoreOriginalSpan: !resizeSuccess);
            e.Pointer.Capture(null);
            if (resizeSuccess)
            {
                e.Handled = true;
            }

            return;
        }

        if (!_isDesktopComponentDragActive || _desktopComponentDrag is null)
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
        if (_isDesktopComponentResizeActive)
        {
            CancelDesktopComponentResize(restoreOriginalSpan: true);
            return;
        }

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
        var pitch = CurrentDesktopPitch;
        Canvas.SetLeft(_desktopComponentDragGhost, column * pitch);
        Canvas.SetTop(_desktopComponentDragGhost, row * pitch);
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

        var pitch = CurrentDesktopPitch;
        if (pitch <= 0)
        {
            return false;
        }

        var x = pointerInViewport.X - state.PointerOffset.X;
        var y = pointerInViewport.Y - state.PointerOffset.Y;

        column = (int)Math.Floor(x / pitch);
        row = (int)Math.Floor(y / pitch);

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
        ComponentLibraryCategoryPagesContainer.Width = double.NaN;
        ComponentLibraryCategoryPagesContainer.Height = double.NaN;
        ComponentLibraryCategoryPagesHost.Width = double.NaN;
        ComponentLibraryCategoryPagesHost.Height = double.NaN;

        if (categoryCount == 0)
        {
            _componentLibraryCategoryIndex = 0;
            _componentLibraryActiveCategoryId = null;
            UpdateComponentLibraryComponentNavigationButtons();
            return;
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

        ComponentLibraryCategoryPagesContainer.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        for (var i = 0; i < categoryCount; i++)
        {
            var category = _componentLibraryCategories[i];
            var isSelected = i == _componentLibraryCategoryIndex;
            var row = new RowDefinition(GridLength.Auto);
            ComponentLibraryCategoryPagesContainer.RowDefinitions.Add(row);

            var icon = new SymbolIcon
            {
                Symbol = category.Icon,
                IconVariant = IconVariant.Regular,
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center
            };

            var title = new TextBlock
            {
                Text = category.Title,
                FontSize = 15,
                FontWeight = isSelected ? FontWeight.Bold : FontWeight.SemiBold,
                Foreground = GetThemeBrush("AdaptiveTextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var contentGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 10,
                Children = { icon, title }
            };
            Grid.SetColumn(icon, 0);
            Grid.SetColumn(title, 1);

            var itemButton = new Button
            {
                Tag = i,
                Margin = new Thickness(0, 0, 0, i < categoryCount - 1 ? 8 : 0),
                Padding = new Thickness(12, 10),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = isSelected
                    ? GetThemeBrush("AdaptiveNavItemSelectedBackgroundBrush")
                    : GetThemeBrush("AdaptiveNavItemBackgroundBrush"),
                BorderBrush = GetThemeBrush("AdaptiveButtonBorderBrush"),
                BorderThickness = new Thickness(isSelected ? 1.5 : 1),
                Content = contentGrid
            };
            itemButton.Click += OnComponentLibraryCategoryItemClick;

            Grid.SetRow(itemButton, i);
            Grid.SetColumn(itemButton, 0);
            ComponentLibraryCategoryPagesContainer.Children.Add(itemButton);
        }

        _componentLibraryCategoryHostTransform = null;
        _componentLibraryCategoryPageWidth = 0;

        if (ComponentLibraryBackTextBlock is not null)
        {
            ComponentLibraryBackTextBlock.Text = L("common.back", "Back");
        }
    }

    private IReadOnlyList<ComponentLibraryCategory> GetComponentLibraryCategories()
    {
        var descriptors = _componentRuntimeRegistry.GetDesktopComponents();
        if (descriptors.Count == 0)
        {
            return Array.Empty<ComponentLibraryCategory>();
        }

        return descriptors
            .GroupBy(descriptor => descriptor.Definition.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var categoryId = string.IsNullOrWhiteSpace(group.Key) ? "Other" : group.Key.Trim();
                var components = group
                    .OrderBy(descriptor => descriptor.Definition.DisplayName, StringComparer.OrdinalIgnoreCase)
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
        if (string.Equals(categoryId, "Clock", StringComparison.OrdinalIgnoreCase))
        {
            return Symbol.Clock;
        }

        if (string.Equals(categoryId, "Date", StringComparison.OrdinalIgnoreCase))
        {
            return Symbol.CalendarDate;
        }

        if (string.Equals(categoryId, "Weather", StringComparison.OrdinalIgnoreCase))
        {
            return Symbol.WeatherSunny;
        }

        if (string.Equals(categoryId, "Board", StringComparison.OrdinalIgnoreCase))
        {
            return Symbol.Edit;
        }

        if (string.Equals(categoryId, "Media", StringComparison.OrdinalIgnoreCase))
        {
            return Symbol.Play;
        }

        if (string.Equals(categoryId, "Info", StringComparison.OrdinalIgnoreCase))
        {
            return Symbol.Apps;
        }

        return Symbol.Apps;
    }

    private string GetLocalizedComponentLibraryCategoryTitle(string categoryId)
    {
        if (string.Equals(categoryId, "Clock", StringComparison.OrdinalIgnoreCase))
        {
            return L("component_category.clock", "Clock");
        }

        if (string.Equals(categoryId, "Date", StringComparison.OrdinalIgnoreCase))
        {
            return L("component_category.date", "Calendar");
        }

        if (string.Equals(categoryId, "Weather", StringComparison.OrdinalIgnoreCase))
        {
            return L("component_category.weather", "Weather");
        }

        if (string.Equals(categoryId, "Board", StringComparison.OrdinalIgnoreCase))
        {
            return L("component_category.board", "Board");
        }

        if (string.Equals(categoryId, "Media", StringComparison.OrdinalIgnoreCase))
        {
            return L("component_category.media", "Media");
        }

        if (string.Equals(categoryId, "Info", StringComparison.OrdinalIgnoreCase))
        {
            return L("component_category.info", "Info");
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
        UpdateComponentLibraryComponentNavigationButtons();
    }

    private void UpdateComponentLibraryComponentNavigationButtons()
    {
        if (ComponentLibraryPrevComponentButton is null || ComponentLibraryNextComponentButton is null)
        {
            return;
        }

        var maxIndex = Math.Max(0, _componentLibraryActiveComponents.Count - 1);
        var hasMultiplePages = maxIndex > 0;

        ComponentLibraryPrevComponentButton.IsVisible = hasMultiplePages;
        ComponentLibraryNextComponentButton.IsVisible = hasMultiplePages;

        if (!hasMultiplePages)
        {
            ComponentLibraryPrevComponentButton.IsEnabled = false;
            ComponentLibraryNextComponentButton.IsEnabled = false;
            return;
        }

        ComponentLibraryPrevComponentButton.IsEnabled = _componentLibraryComponentIndex > 0;
        ComponentLibraryNextComponentButton.IsEnabled = _componentLibraryComponentIndex < maxIndex;
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

        ClearTimeZoneServiceBindings(ComponentLibraryComponentPagesContainer.Children.OfType<Control>().ToList());
        ComponentLibraryComponentPagesContainer.Children.Clear();
        ComponentLibraryComponentPagesContainer.RowDefinitions.Clear();
        ComponentLibraryComponentPagesContainer.ColumnDefinitions.Clear();
        if (componentCount == 0)
        {
            _componentLibraryComponentIndex = 0;
            UpdateComponentLibraryComponentNavigationButtons();
            return;
        }

        var viewportWidth = ComponentLibraryComponentViewport.Bounds.Width;
        if (viewportWidth <= 1)
        {
            if (ComponentLibraryComponentViewport.Parent is Control parent && parent.Bounds.Width > 1)
            {
                // Parent includes left/right nav buttons; reserve space to get true viewport width.
                viewportWidth = Math.Max(1, parent.Bounds.Width - 96);
            }
            else if (ComponentLibraryWindow is not null)
            {
                viewportWidth = Math.Max(1, ComponentLibraryWindow.Bounds.Width - 150);
            }
        }

        var viewportHeight = ComponentLibraryComponentViewport.Bounds.Height;
        if (viewportHeight <= 1)
        {
            if (ComponentLibraryComponentViewport.Parent is Control parent && parent.Bounds.Height > 1)
            {
                viewportHeight = Math.Max(1, parent.Bounds.Height);
            }
            else if (ComponentLibraryWindow is not null)
            {
                viewportHeight = Math.Max(1, ComponentLibraryWindow.Bounds.Height - 170);
            }
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
            var descriptor = _componentLibraryActiveComponents[i];
            var definition = descriptor.Definition;
            if (!definition.AllowDesktopPlacement)
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
            var previewMaxWidth = _componentLibraryComponentPageWidth * 0.94;
            var previewMaxHeight = viewportHeight * 0.86;
            var previewSpan = NormalizeComponentCellSpan(
                definition.Id,
                (definition.MinWidthCells, definition.MinHeightCells));
            var previewCellSize = Math.Min(
                previewMaxWidth / Math.Max(1, previewSpan.WidthCells),
                previewMaxHeight / Math.Max(1, previewSpan.HeightCells));
            previewCellSize = Math.Clamp(previewCellSize, 24, 96);

            var previewWidth = previewSpan.WidthCells * previewCellSize;
            var previewHeight = previewSpan.HeightCells * previewCellSize;
            var renderCellSize = Math.Clamp(previewCellSize * 1.15, 26, 110);

            var previewControl = descriptor.CreateControl(renderCellSize, _timeZoneService, _weatherDataService);

            var previewSurface = new Border
            {
                Width = previewSpan.WidthCells * renderCellSize,
                Height = previewSpan.HeightCells * renderCellSize,
                Background = Brushes.Transparent,
                Child = previewControl
            };

            var previewViewbox = new Viewbox
            {
                Width = previewWidth,
                Height = previewHeight,
                Stretch = Stretch.Uniform,
                Child = previewSurface
            };

            var previewBorder = new Border
            {
                Width = previewWidth,
                Height = previewHeight,
                ClipToBounds = false,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Child = previewViewbox,
                Tag = definition.Id
            };
            previewBorder.PointerPressed += OnComponentLibraryComponentPreviewPointerPressed;

            var label = new TextBlock
            {
                Text = GetLocalizedComponentDisplayName(descriptor),
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
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    previewBorder,
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
        UpdateComponentLibraryComponentNavigationButtons();
    }

    private void ClearComponentLibraryPreviewControls()
    {
        if (ComponentLibraryComponentPagesContainer is null)
        {
            return;
        }

        ClearTimeZoneServiceBindings(ComponentLibraryComponentPagesContainer.Children.OfType<Control>().ToList());
        ComponentLibraryComponentPagesContainer.Children.Clear();
        ComponentLibraryComponentPagesContainer.RowDefinitions.Clear();
        ComponentLibraryComponentPagesContainer.ColumnDefinitions.Clear();
    }

    private string GetLocalizedComponentDisplayName(DesktopComponentRuntimeDescriptor descriptor)
    {
        return L(descriptor.DisplayNameLocalizationKey, descriptor.Definition.DisplayName);
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

    private bool _isComponentLibraryWindowDragging;
    private Point _componentLibraryWindowDragStartPoint;
    private Thickness _componentLibraryWindowOriginalMargin;
    private bool _isComponentLibraryWindowPositionCustomized;
    
    private void OnComponentLibraryWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ComponentLibraryWindow is null || !_isComponentLibraryOpen)
        {
            return;
        }

        var point = e.GetPosition(ComponentLibraryWindow);
        if (point.Y > 40) // 閺嶅洭顣介弽蹇涚彯鎼达妇瀹虫稉?0px
        {
            return;
        }

        _isComponentLibraryWindowDragging = true;
        _componentLibraryWindowDragStartPoint = e.GetPosition(this);
        _componentLibraryWindowOriginalMargin = ComponentLibraryWindow.Margin;
        
        e.Pointer.Capture(ComponentLibraryWindow);
        e.Handled = true;
    }

    private void OnComponentLibraryWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isComponentLibraryWindowDragging || ComponentLibraryWindow is null)
        {
            return;
        }

        var currentPoint = e.GetPosition(this);
        var delta = currentPoint - _componentLibraryWindowDragStartPoint;
        
        var newMargin = new Thickness(
            Math.Max(10, _componentLibraryWindowOriginalMargin.Left + delta.X),
            Math.Max(10, _componentLibraryWindowOriginalMargin.Top + delta.Y),
            Math.Max(10, _componentLibraryWindowOriginalMargin.Right - delta.X),
            Math.Max(10, _componentLibraryWindowOriginalMargin.Bottom - delta.Y)
        );
        
        ComponentLibraryWindow.Margin = newMargin;
        e.Handled = true;
    }

    private void OnComponentLibraryWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isComponentLibraryWindowDragging)
        {
            return;
        }

        _isComponentLibraryWindowDragging = false;
        e.Pointer.Capture(null);
        
        if (ComponentLibraryWindow is not null)
        {
            SaveComponentLibraryWindowPosition();
        }
        
        e.Handled = true;
    }

    private void OnComponentLibraryBackClick(object? sender, RoutedEventArgs e)
    {
        ShowComponentLibraryCategoryView();
        BuildComponentLibraryCategoryPages();
    }

    private void OnComponentLibraryCategoryItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not int categoryIndex ||
            _componentLibraryCategories.Count == 0)
        {
            return;
        }

        _componentLibraryCategoryIndex = Math.Clamp(categoryIndex, 0, Math.Max(0, _componentLibraryCategories.Count - 1));
        OpenComponentLibraryCurrentCategory();
    }

    private void OnComponentLibraryPrevComponentClick(object? sender, RoutedEventArgs e)
    {
        if (_componentLibraryActiveComponents.Count <= 1)
        {
            return;
        }

        _componentLibraryComponentIndex = Math.Max(0, _componentLibraryComponentIndex - 1);
        ApplyComponentLibraryComponentOffset();
    }

    private void OnComponentLibraryNextComponentClick(object? sender, RoutedEventArgs e)
    {
        var maxIndex = Math.Max(0, _componentLibraryActiveComponents.Count - 1);
        if (maxIndex <= 0)
        {
            return;
        }

        _componentLibraryComponentIndex = Math.Min(maxIndex, _componentLibraryComponentIndex + 1);
        ApplyComponentLibraryComponentOffset();
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

    private void SaveComponentLibraryWindowPosition()
    {
        if (ComponentLibraryWindow is null)
        {
            return;
        }

        var margin = ComponentLibraryWindow.Margin;
        _savedComponentLibraryMargin = margin;
        _isComponentLibraryWindowPositionCustomized = true;
    }

    private void RestoreComponentLibraryWindowPosition()
    {
        if (ComponentLibraryWindow is null)
        {
            return;
        }

        ComponentLibraryWindow.Margin = _savedComponentLibraryMargin;
    }

    private Thickness _savedComponentLibraryMargin = new Thickness(24, 24, 24, 100);

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

