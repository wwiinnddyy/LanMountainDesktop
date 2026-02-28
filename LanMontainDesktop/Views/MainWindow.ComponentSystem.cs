using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using LanMontainDesktop.ComponentSystem;
using LanMontainDesktop.Models;

namespace LanMontainDesktop.Views;

public partial class MainWindow
{
    private void OnOpenComponentLibraryClick(object? sender, RoutedEventArgs e)
    {
        if (_isComponentLibraryOpen)
        {
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
        var showComponentLibrary = _isSettingsOpen || _isComponentLibraryOpen;

        BackToWindowsButton.IsVisible = showMinimize;
        OpenComponentLibraryButton.IsVisible = showComponentLibrary;
        OpenSettingsButton.IsVisible = showSettings;
        WallpaperPreviewBackButtonVisual.IsVisible = showMinimize;
        WallpaperPreviewComponentLibraryVisual.IsVisible = showComponentLibrary;
        WallpaperPreviewSettingsButtonIcon.IsVisible = showSettings;

        if (TaskbarFixedActionsHost is not null)
        {
            TaskbarFixedActionsHost.IsVisible = showMinimize;
        }

        if (TaskbarSettingsActionHost is not null)
        {
            TaskbarSettingsActionHost.IsVisible = showSettings || showComponentLibrary;
        }

        if (WallpaperPreviewTaskbarFixedActionsHost is not null)
        {
            WallpaperPreviewTaskbarFixedActionsHost.IsVisible = showMinimize;
        }

        if (WallpaperPreviewTaskbarSettingsActionHost is not null)
        {
            WallpaperPreviewTaskbarSettingsActionHost.IsVisible = showSettings || showComponentLibrary;
        }

        var dynamicActions = ResolveDynamicTaskbarActions(context);
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
        ComponentLibraryWindow.IsVisible = true;
        ComponentLibraryWindow.Opacity = 0;
        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());

        Dispatcher.UIThread.Post(() =>
        {
            if (!_isComponentLibraryOpen || ComponentLibraryWindow is null)
            {
                return;
            }

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

    private IReadOnlyList<TaskbarActionItem> ResolveDynamicTaskbarActions(TaskbarContext context)
    {
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

        if (WallpaperPreviewTaskbarDynamicActionsPanel is not null)
        {
            WallpaperPreviewTaskbarDynamicActionsPanel.Children.Clear();
        }

        if (actions.Count == 0 ||
            TaskbarDynamicActionsPanel is null ||
            WallpaperPreviewTaskbarDynamicActionsPanel is null)
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
                Foreground = Foreground
            };

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
            WallpaperPreviewTaskbarDynamicActionsPanel.Children.Add(previewBorder);
        }
    }
}
