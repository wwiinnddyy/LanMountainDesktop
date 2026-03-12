﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views;

public partial class MainWindow
{
    private const int MinDesktopPageCount = 1;
    private const int MaxDesktopPageCount = 12;
    private enum LauncherEntryKind
    {
        Folder,
        Shortcut
    }

    private sealed record LauncherHiddenItemToken(LauncherEntryKind Kind, string Key);

    private sealed record LauncherHiddenItemView(
        LauncherEntryKind Kind,
        string Key,
        string DisplayName,
        string Monogram,
        Bitmap? IconBitmap);

    private readonly WindowsStartMenuService _windowsStartMenuService = new();
    private readonly LinuxDesktopEntryService _linuxDesktopEntryService = new();
    private readonly Dictionary<string, Bitmap> _launcherIconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<StartMenuFolderNode> _launcherFolderStack = [];
    private readonly HashSet<string> _hiddenLauncherFolderPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hiddenLauncherAppPaths = new(StringComparer.OrdinalIgnoreCase);
    private Button? _selectedLauncherTileButton;
    private LauncherEntryKind? _selectedLauncherEntryKind;
    private string? _selectedLauncherEntryKey;
    private StartMenuFolderNode _startMenuRoot = new("All Apps", string.Empty);
    private byte[]? _launcherFolderIconPngBytes;
    private Bitmap? _launcherFolderIconBitmap;
    private int _desktopPageCount = MinDesktopPageCount;
    private int _currentDesktopSurfaceIndex;
    private double _desktopSurfacePageWidth;
    private TranslateTransform? _desktopPagesHostTransform;
    private bool _isDesktopSwipeActive;
    private bool _isDesktopSwipeDirectionLocked;
    private Point _desktopSwipeStartPoint;
    private Point _desktopSwipeCurrentPoint;
    private Point _desktopSwipeLastPoint;
    private long _desktopSwipeLastTimestamp;
    private double _desktopSwipeVelocityX;
    private double _desktopSwipeBaseOffset;

    private int LauncherSurfaceIndex => Math.Max(MinDesktopPageCount, _desktopPageCount);

    private int TotalSurfaceCount => LauncherSurfaceIndex + 1;

    private void InitializeDesktopSurfaceState(DesktopLayoutSettingsSnapshot snapshot)
    {
        var loadedPageCount = snapshot.DesktopPageCount <= 0 ? MinDesktopPageCount : snapshot.DesktopPageCount;
        _desktopPageCount = Math.Clamp(loadedPageCount, MinDesktopPageCount, MaxDesktopPageCount);
        _currentDesktopSurfaceIndex = Math.Clamp(snapshot.CurrentDesktopSurfaceIndex, 0, LauncherSurfaceIndex);
    }

    private void InitializeLauncherVisibilitySettings(LauncherSettingsSnapshot snapshot)
    {
        _hiddenLauncherFolderPaths.Clear();
        if (snapshot.HiddenLauncherFolderPaths is not null)
        {
            foreach (var folderPath in snapshot.HiddenLauncherFolderPaths)
            {
                var key = NormalizeLauncherHiddenKey(folderPath);
                if (!string.IsNullOrWhiteSpace(key))
                {
                    _hiddenLauncherFolderPaths.Add(key);
                }
            }
        }

        _hiddenLauncherAppPaths.Clear();
        if (snapshot.HiddenLauncherAppPaths is not null)
        {
            foreach (var appPath in snapshot.HiddenLauncherAppPaths)
            {
                var key = NormalizeLauncherHiddenKey(appPath);
                if (!string.IsNullOrWhiteSpace(key))
                {
                    _hiddenLauncherAppPaths.Add(key);
                }
            }
        }
    }

    private void InitializeDesktopSurfaceSwipeHandlers()
    {
        // Capture swipe intent before child controls consume pointer events.
        AddHandler(PointerPressedEvent, OnDesktopPagesPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnDesktopPagesPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnDesktopPagesPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, OnDesktopPagesPointerCaptureLost, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private async void LoadLauncherEntriesAsync()
    {
        try
        {
            var loadResult = await Task.Run(() =>
            {
                var loadedRoot = OperatingSystem.IsLinux()
                    ? _linuxDesktopEntryService.Load()
                    : _windowsStartMenuService.Load();
                var folderIconBytes = OperatingSystem.IsWindows()
                    ? WindowsIconService.TryGetSystemFolderIconPngBytes()
                    : null;
                return (Root: loadedRoot, FolderIcon: folderIconBytes);
            });
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _startMenuRoot = loadResult.Root;
                _launcherFolderIconPngBytes = loadResult.FolderIcon;
                _launcherFolderIconBitmap?.Dispose();
                _launcherFolderIconBitmap = null;
                RenderLauncherRootTiles();
                RenderLauncherHiddenItemsList();
            }, DispatcherPriority.Background);
        }
        catch
        {
            _startMenuRoot = new StartMenuFolderNode("All Apps", string.Empty);
            _launcherFolderIconPngBytes = null;
            _launcherFolderIconBitmap?.Dispose();
            _launcherFolderIconBitmap = null;
            RenderLauncherRootTiles();
            RenderLauncherHiddenItemsList();
        }
    }

    private void UpdateDesktopSurfaceLayout(DesktopGridMetrics gridMetrics)
    {
        if (DesktopPagesViewport is null ||
            DesktopPagesHost is null ||
            DesktopPagesContainer is null ||
            LauncherPagePanel is null)
        {
            return;
        }

        _desktopPagesHostTransform = DesktopPagesHost.RenderTransform as TranslateTransform;
        if (_desktopPagesHostTransform is null)
        {
            _desktopPagesHostTransform = new TranslateTransform();
            DesktopPagesHost.RenderTransform = _desktopPagesHostTransform;
        }

        var viewportRow = gridMetrics.RowCount > 2 ? 1 : 0;
        var viewportRowSpan = gridMetrics.RowCount > 2 ? gridMetrics.RowCount - 2 : 1;
        var pageWidth = Math.Max(1, gridMetrics.GridWidthPx);
        var pageHeight = Math.Max(
            1,
            viewportRowSpan * gridMetrics.CellSize + Math.Max(0, viewportRowSpan - 1) * gridMetrics.GapPx);

        Grid.SetRow(DesktopPagesViewport, viewportRow);
        Grid.SetColumn(DesktopPagesViewport, 0);
        Grid.SetRowSpan(DesktopPagesViewport, viewportRowSpan);
        Grid.SetColumnSpan(DesktopPagesViewport, gridMetrics.ColumnCount);
        DesktopPagesViewport.Width = pageWidth;
        DesktopPagesViewport.Height = pageHeight;
        if (DesktopEditDragLayer is not null)
        {
            DesktopEditDragLayer.Width = pageWidth;
            DesktopEditDragLayer.Height = pageHeight;
        }

        DesktopPagesHost.RowDefinitions.Clear();
        DesktopPagesHost.RowDefinitions.Add(new RowDefinition(new GridLength(pageHeight, GridUnitType.Pixel)));
        DesktopPagesHost.ColumnDefinitions.Clear();
        DesktopPagesHost.ColumnDefinitions.Add(
            new ColumnDefinition(new GridLength(pageWidth * _desktopPageCount, GridUnitType.Pixel)));
        DesktopPagesHost.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(pageWidth, GridUnitType.Pixel)));
        DesktopPagesHost.Width = pageWidth * TotalSurfaceCount;
        DesktopPagesHost.Height = pageHeight;

        DesktopPagesContainer.RowDefinitions.Clear();
        DesktopPagesContainer.RowDefinitions.Add(new RowDefinition(new GridLength(pageHeight, GridUnitType.Pixel)));
        DesktopPagesContainer.ColumnDefinitions.Clear();
        ClearTimeZoneServiceBindings(DesktopPagesContainer.Children.OfType<Control>().ToList());
        DesktopPagesContainer.Children.Clear();
        DesktopPagesContainer.Width = pageWidth * _desktopPageCount;
        DesktopPagesContainer.Height = pageHeight;
        _desktopPageComponentGrids.Clear();
        for (var index = 0; index < _desktopPageCount; index++)
        {
            DesktopPagesContainer.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(pageWidth, GridUnitType.Pixel)));

            var pageGrid = new Grid
            {
                Width = pageWidth,
                Height = pageHeight,
                RowSpacing = gridMetrics.GapPx,
                ColumnSpacing = gridMetrics.GapPx,
                Background = Brushes.Transparent,
                ShowGridLines = false
            };

            for (var row = 0; row < viewportRowSpan; row++)
            {
                pageGrid.RowDefinitions.Add(new RowDefinition(new GridLength(gridMetrics.CellSize, GridUnitType.Pixel)));
            }

            for (var col = 0; col < gridMetrics.ColumnCount; col++)
            {
                pageGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(gridMetrics.CellSize, GridUnitType.Pixel)));
            }

            _desktopPageComponentGrids[index] = pageGrid;
            RestoreDesktopPageComponents(index);

            Grid.SetColumn(pageGrid, index);
            Grid.SetRow(pageGrid, 0);
            DesktopPagesContainer.Children.Add(pageGrid);
        }

        Grid.SetColumn(LauncherPagePanel, 1);
        Grid.SetRow(LauncherPagePanel, 0);

        // 为启动台添加安全边距以确保圆角不被裁剪
        var launcherMargin = Math.Clamp(gridMetrics.CellSize * 0.15, 6, 16);
        LauncherPagePanel.Margin = new Thickness(launcherMargin);
        LauncherPagePanel.Width = Math.Max(1, pageWidth - launcherMargin * 2);
        LauncherPagePanel.Height = Math.Max(1, pageHeight - launcherMargin * 2);
        LauncherPagePanel.MaxWidth = pageWidth - launcherMargin * 2;
        LauncherPagePanel.MaxHeight = pageHeight - launcherMargin * 2;

        if (LauncherFolderPanel is not null)
        {
            LauncherFolderPanel.MaxWidth = Math.Max(320, pageWidth - 96);
            LauncherFolderPanel.MaxHeight = Math.Max(220, pageHeight - 96);
        }

        // 更新启动台图标布局
        UpdateLauncherTileLayout();

        _desktopSurfacePageWidth = pageWidth;
        ClampSurfaceIndex();
        ApplyDesktopSurfaceOffset();
    }

    private void UpdateLauncherTileLayout()
    {
        if (LauncherRootTilePanel is null || LauncherPagePanel is null)
        {
            return;
        }

        // 获取启动台面板的实际可用宽度（减去Padding）
        var availableWidth = Math.Max(1, LauncherPagePanel.Bounds.Width - 36); // 18px padding on each side
        var availableHeight = Math.Max(1, LauncherPagePanel.Bounds.Height - 100); // 预留标题空间

        if (availableWidth <= 1 || availableHeight <= 1)
        {
            // 如果尺寸还未计算，使用默认值
            availableWidth = 600;
            availableHeight = 400;
        }

        // 计算最佳图标尺寸
        // 目标：每行显示4-8个图标，根据屏幕宽度调整
        const int minColumns = 4;
        const int maxColumns = 8;
        const double targetAspectRatio = 1.2; // 图标宽高比

        // 计算每列可以显示的图标数量
        var optimalColumnCount = Math.Clamp((int)Math.Floor(availableWidth / 120), minColumns, maxColumns);

        // 根据列数计算图标尺寸
        var tileWidth = Math.Floor(availableWidth / optimalColumnCount) - 12; // 12px spacing
        var tileHeight = Math.Min(tileWidth / targetAspectRatio, availableHeight / 4); // 至少显示4行

        // 确保最小尺寸
        tileWidth = Math.Max(tileWidth, 100);
        tileHeight = Math.Max(tileHeight, 80);

        // 更新WrapPanel的Item尺寸
        LauncherRootTilePanel.Width = availableWidth;

        // 更新所有子元素的尺寸
        foreach (var child in LauncherRootTilePanel.Children)
        {
            if (child is Button button)
            {
                button.Width = tileWidth;
                button.Height = tileHeight;
            }
        }

        // 同样更新文件夹视图的图标尺寸
        if (LauncherFolderTilePanel is not null)
        {
            LauncherFolderTilePanel.Width = availableWidth;
            foreach (var child in LauncherFolderTilePanel.Children)
            {
                if (child is Button button)
                {
                    button.Width = tileWidth;
                    button.Height = tileHeight;
                }
            }
        }
    }

    private void ClampSurfaceIndex()
    {
        _currentDesktopSurfaceIndex = Math.Clamp(_currentDesktopSurfaceIndex, 0, LauncherSurfaceIndex);
    }

    private IBrush GetThemeBrush(string key)
    {
        if (Resources.TryGetResource(key, ActualThemeVariant, out var resource) && resource is IBrush brush)
        {
            return brush;
        }

        return Brushes.Transparent;
    }

    private void ApplyDesktopSurfaceOffset()
    {
        if (_desktopPagesHostTransform is null || _desktopSurfacePageWidth <= 0)
        {
            return;
        }

        var targetOffset = -_currentDesktopSurfaceIndex * _desktopSurfacePageWidth;
        _desktopPagesHostTransform.X = targetOffset;

        if (_currentDesktopSurfaceIndex != LauncherSurfaceIndex)
        {
            CloseLauncherFolderOverlay();
            ClearSelectedLauncherTile(refreshTaskbar: false);
        }

        UpdateDesktopPageAwareComponentContext();
    }

    private void MoveSurfaceBy(int delta)
    {
        if (delta == 0)
        {
            return;
        }

        MoveSurfaceTo(_currentDesktopSurfaceIndex + delta);
    }

    private void MoveSurfaceTo(int targetIndex)
    {
        var target = Math.Clamp(targetIndex, 0, LauncherSurfaceIndex);
        if (target == _currentDesktopSurfaceIndex)
        {
            ApplyDesktopSurfaceOffset();
            return;
        }

        _currentDesktopSurfaceIndex = target;
        ApplyDesktopSurfaceOffset();
        PersistSettings();
    }

    private bool CanSwipeDesktopSurface()
    {
        return !_isSettingsOpen &&
               !_isComponentLibraryOpen &&
               !_isDesktopComponentDragActive &&
               !_isDesktopComponentResizeActive &&
               _desktopSurfacePageWidth > 1;
    }

    private void OnDesktopPagesPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!TryGetPointerPositionInDesktopViewport(e, out var pointerInViewport))
        {
            return;
        }

        // 如果在组件编辑模式下点击空白区域，取消选中（组件或启动台图标）
        if (_isComponentLibraryOpen &&
            (_selectedDesktopComponentHost is not null || _selectedLauncherTileButton is not null))
        {
            if (!IsInteractivePointerSource(e.Source))
            {
                ClearDesktopComponentSelection();
                ClearSelectedLauncherTile(refreshTaskbar: false);
                ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
            }
        }

        if (!CanSwipeDesktopSurface())
        {
            return;
        }

        if (IsInteractivePointerSource(e.Source))
        {
            return;
        }

        if (IsDesktopSwipeBlockedPointerSource(e.Source))
        {
            return;
        }

        if (!e.GetCurrentPoint(DesktopPagesViewport).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isDesktopSwipeActive = true;
        _isDesktopSwipeDirectionLocked = false;
        _desktopSwipeStartPoint = pointerInViewport;
        _desktopSwipeCurrentPoint = _desktopSwipeStartPoint;
        _desktopSwipeLastPoint = _desktopSwipeStartPoint;
        _desktopSwipeVelocityX = 0;
        _desktopSwipeLastTimestamp = Stopwatch.GetTimestamp();
        _desktopSwipeBaseOffset = -_currentDesktopSurfaceIndex * _desktopSurfacePageWidth;
    }

    private static bool IsInteractivePointerSource(object? source)
    {
        if (source is not Visual visual)
        {
            return false;
        }

        foreach (var node in visual.GetSelfAndVisualAncestors())
        {
            if (node is Control control)
            {
                // Avoid swiping pages when interacting with desktop components/widgets.
                if (control.Classes.Contains("desktop-component") ||
                    control.Classes.Contains("desktop-component-host"))
                {
                    return true;
                }
            }

            if (node is Button or TextBox or ComboBox or ListBoxItem or Slider or ToggleSwitch)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDesktopSwipeBlockedPointerSource(object? source)
    {
        if (source is not Visual visual)
        {
            return false;
        }

        var pendingNodes = new Stack<object>();
        var visitedNodes = new HashSet<object>(ReferenceEqualityComparer.Instance);
        pendingNodes.Push(visual);

        while (pendingNodes.Count > 0)
        {
            var node = pendingNodes.Pop();
            if (!visitedNodes.Add(node))
            {
                continue;
            }

            if (IsDesktopSwipeBlockingNode(node))
            {
                return true;
            }

            if (node is StyledElement styledElement &&
                styledElement.TemplatedParent is { } templatedParent)
            {
                pendingNodes.Push(templatedParent);
            }

            if (node is Visual currentVisual &&
                currentVisual.GetVisualParent() is { } parentVisual)
            {
                pendingNodes.Push(parentVisual);
            }
        }

        return false;
    }

    private static bool IsDesktopSwipeBlockingNode(object node)
    {
        if (node is Button or TextBox or ComboBox or Slider or ToggleSwitch or ListBoxItem or ScrollViewer)
        {
            return true;
        }

        if (node is Control control &&
            (control.Classes.Contains("study-history-action-button") ||
             control.Classes.Contains("desktop-component") ||
             control.Classes.Contains("desktop-component-host")))
        {
            return true;
        }

        var typeName = node.GetType().Name;
        return typeName.Contains("Button", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("WebView", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("ScrollBar", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("NumericUpDown", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("TextPresenter", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetPointerPositionInDesktopViewport(PointerEventArgs e, out Point point)
    {
        point = default;
        if (DesktopPagesViewport is null)
        {
            return false;
        }

        point = e.GetPosition(DesktopPagesViewport);
        if (_isDesktopSwipeActive && _isDesktopSwipeDirectionLocked)
        {
            return true;
        }

        var bounds = DesktopPagesViewport.Bounds;
        return bounds.Width > 1 &&
               bounds.Height > 1 &&
               point.X >= 0 &&
               point.Y >= 0 &&
               point.X <= bounds.Width &&
               point.Y <= bounds.Height;
    }

    private void UpdateDesktopSwipeVelocity(Point pointer)
    {
        var now = Stopwatch.GetTimestamp();
        if (_desktopSwipeLastTimestamp > 0)
        {
            var elapsedSeconds = (now - _desktopSwipeLastTimestamp) / (double)Stopwatch.Frequency;
            if (elapsedSeconds > 0.0001)
            {
                var instantVelocity = (pointer.X - _desktopSwipeLastPoint.X) / elapsedSeconds;
                _desktopSwipeVelocityX = _desktopSwipeVelocityX * 0.7 + instantVelocity * 0.3;
            }
        }

        _desktopSwipeLastPoint = pointer;
        _desktopSwipeLastTimestamp = now;
    }

    private void OnDesktopPagesPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDesktopSwipeActive || !TryGetPointerPositionInDesktopViewport(e, out var pointerInViewport))
        {
            return;
        }

        if (_desktopPagesHostTransform is null || DesktopPagesViewport is null)
        {
            return;
        }

        _desktopSwipeCurrentPoint = pointerInViewport;
        UpdateDesktopSwipeVelocity(pointerInViewport);
        var deltaX = _desktopSwipeCurrentPoint.X - _desktopSwipeStartPoint.X;
        var deltaY = _desktopSwipeCurrentPoint.Y - _desktopSwipeStartPoint.Y;

        if (!_isDesktopSwipeDirectionLocked)
        {
            const double activationThreshold = 14;
            const double horizontalBias = 1.15;
            var absDeltaX = Math.Abs(deltaX);
            var absDeltaY = Math.Abs(deltaY);

            if (absDeltaY >= activationThreshold && absDeltaY > absDeltaX * horizontalBias)
            {
                CancelDesktopSwipeInteraction(e.Pointer);
                return;
            }

            if (absDeltaX < activationThreshold || absDeltaX <= absDeltaY * horizontalBias)
            {
                return;
            }

            _isDesktopSwipeDirectionLocked = true;
            if (e.Pointer.Captured != DesktopPagesViewport)
            {
                e.Pointer.Capture(DesktopPagesViewport);
            }
        }

        var minOffset = -LauncherSurfaceIndex * _desktopSurfacePageWidth;
        var tentative = _desktopSwipeBaseOffset + deltaX;
        if (tentative > 0)
        {
            tentative *= 0.24;
        }
        else if (tentative < minOffset)
        {
            tentative = minOffset + (tentative - minOffset) * 0.24;
        }

        _desktopPagesHostTransform.X = tentative;
        e.Handled = true;
    }

    private void OnDesktopPagesPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (EndDesktopSwipeInteraction(e.Pointer))
        {
            e.Handled = true;
        }
    }

    private void OnDesktopPagesPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndDesktopSwipeInteraction(e.Pointer);
    }

    private void CancelDesktopSwipeInteraction(IPointer? pointer)
    {
        if (!_isDesktopSwipeActive)
        {
            return;
        }

        var wasDirectionLocked = _isDesktopSwipeDirectionLocked;
        if (pointer?.Captured == DesktopPagesViewport)
        {
            pointer.Capture(null);
        }

        _isDesktopSwipeActive = false;
        _isDesktopSwipeDirectionLocked = false;
        _desktopSwipeVelocityX = 0;
        _desktopSwipeLastTimestamp = 0;
        if (wasDirectionLocked)
        {
            ApplyDesktopSurfaceOffset();
        }
    }

    private bool EndDesktopSwipeInteraction(IPointer? pointer)
    {
        if (!_isDesktopSwipeActive)
        {
            return false;
        }

        var wasDirectionLocked = _isDesktopSwipeDirectionLocked;
        _isDesktopSwipeActive = false;
        _isDesktopSwipeDirectionLocked = false;
        if (pointer?.Captured == DesktopPagesViewport)
        {
            pointer.Capture(null);
        }

        _desktopSwipeLastTimestamp = 0;
        if (!wasDirectionLocked)
        {
            _desktopSwipeVelocityX = 0;
            return false;
        }

        var deltaX = _desktopSwipeCurrentPoint.X - _desktopSwipeStartPoint.X;
        var deltaY = _desktopSwipeCurrentPoint.Y - _desktopSwipeStartPoint.Y;
        var absDeltaX = Math.Abs(deltaX);
        var absDeltaY = Math.Abs(deltaY);
        var distanceThreshold = Math.Max(48, _desktopSurfacePageWidth * 0.14);
        var velocityThreshold = Math.Max(860, _desktopSurfacePageWidth * 1.08);
        var predictedDeltaX = deltaX + _desktopSwipeVelocityX * 0.18;
        var predictedOffset = _desktopSwipeBaseOffset + predictedDeltaX;
        var projectedTargetIndex = (int)Math.Round(-predictedOffset / _desktopSurfacePageWidth);
        projectedTargetIndex = Math.Clamp(projectedTargetIndex, 0, LauncherSurfaceIndex);

        var hasDistanceIntent = absDeltaX >= distanceThreshold && absDeltaX > absDeltaY * 1.05;
        var hasVelocityIntent = Math.Abs(_desktopSwipeVelocityX) >= velocityThreshold;

        if (projectedTargetIndex == _currentDesktopSurfaceIndex && (hasDistanceIntent || hasVelocityIntent))
        {
            projectedTargetIndex = Math.Clamp(
                _currentDesktopSurfaceIndex + (deltaX < 0 ? 1 : -1),
                0,
                LauncherSurfaceIndex);
        }

        _desktopSwipeVelocityX = 0;

        if (projectedTargetIndex != _currentDesktopSurfaceIndex)
        {
            MoveSurfaceTo(projectedTargetIndex);
            return true;
        }

        ApplyDesktopSurfaceOffset();
        return hasDistanceIntent || hasVelocityIntent;
    }

    private void OnDesktopPagesPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!CanSwipeDesktopSurface())
        {
            return;
        }

        var prefersHorizontal = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ||
                                e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (!prefersHorizontal)
        {
            return;
        }

        var delta = e.Delta.X != 0 ? e.Delta.X : e.Delta.Y;
        if (Math.Abs(delta) < double.Epsilon)
        {
            return;
        }

        MoveSurfaceBy(delta < 0 ? 1 : -1);
        e.Handled = true;
    }

    private void RenderLauncherRootTiles()
    {
        if (LauncherRootTilePanel is null)
        {
            return;
        }

        ClearSelectedLauncherTile(refreshTaskbar: false);
        LauncherRootTilePanel.Children.Clear();
        var folders = _startMenuRoot.Folders;
        var apps = _startMenuRoot.Apps;

        foreach (var folder in folders)
        {
            if (!IsLauncherFolderVisible(folder))
            {
                continue;
            }

            LauncherRootTilePanel.Children.Add(CreateLauncherFolderTile(folder));
        }

        foreach (var app in apps)
        {
            if (!IsLauncherAppVisible(app))
            {
                continue;
            }

            LauncherRootTilePanel.Children.Add(CreateLauncherAppTile(app));
        }

        if (LauncherRootTilePanel.Children.Count == 0)
        {
            LauncherRootTilePanel.Children.Add(CreateLauncherHintTile(
                GetLauncherEmptyText(),
                string.Empty));
        }

        // 在图标渲染完成后，应用布局计算
        Dispatcher.UIThread.Post(() => UpdateLauncherTileLayout(), DispatcherPriority.Background);
    }

    private Button CreateLauncherFolderTile(StartMenuFolderNode folder)
    {
        var title = folder.Name;
        var subtitle = Lf("launcher.folder_items_format", "{0} apps", folder.TotalAppCount);
        var folderIconBitmap = GetLauncherFolderIconBitmap();
        var folderKey = NormalizeLauncherHiddenKey(folder.RelativePath);
        return CreateLauncherTileButton(
            title,
            subtitle,
            monogram: "DIR",
            iconBitmap: folderIconBitmap,
            () => OpenLauncherFolder(folder),
            LauncherEntryKind.Folder,
            folderKey);
    }

    private Button CreateLauncherAppTile(StartMenuAppEntry app)
    {
        var iconBitmap = GetLauncherIconBitmap(app);
        var monogram = BuildMonogram(app.DisplayName);
        var appKey = NormalizeLauncherHiddenKey(app.RelativePath);
        return CreateLauncherTileButton(
            app.DisplayName,
            subtitle: string.Empty,
            monogram,
            iconBitmap,
            () => LaunchStartMenuEntry(app),
            LauncherEntryKind.Shortcut,
            appKey);
    }

    private Control CreateLauncherHintTile(string title, string subtitle)
    {
        var panel = new StackPanel
        {
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            panel.Children.Add(new TextBlock
            {
                Text = subtitle,
                Opacity = 0.75,
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }

        return new Border
        {
            Classes = { "glass-panel" },
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 12, 12),
            CornerRadius = new CornerRadius(20),
            Child = panel
            // 不设置固定 Width 和 Height，由 UpdateLauncherTileLayout 动态设置
        };
    }

    private Button CreateLauncherTileButton(
        string title,
        string subtitle,
        string monogram,
        Bitmap? iconBitmap,
        Action clickAction,
        LauncherEntryKind entryKind,
        string entryKey)
    {
        Control iconControl = iconBitmap is not null
            ? new Image
            {
                Source = iconBitmap,
                Width = 40,
                Height = 40,
                Stretch = Stretch.Uniform
            }
            : new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(999),
                Background = GetThemeBrush("AdaptiveButtonBackgroundBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                BorderThickness = new Thickness(0),
                Child = new TextBlock
                {
                    Text = monogram,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

        var textPanel = new StackPanel
        {
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        textPanel.Children.Add(new TextBlock
        {
            Text = title,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        });

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            textPanel.Children.Add(new TextBlock
            {
                Text = subtitle,
                Opacity = 0.72,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch
            });
        }

        var content = new StackPanel
        {
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(iconControl);
        content.Children.Add(textPanel);

        var button = new Button
        {
            Classes = { "glass-panel" },
            Margin = new Thickness(0, 0, 12, 12),
            BorderThickness = new Thickness(0),
            BorderBrush = Brushes.Transparent,
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(10),
            Content = content
            // 不设置固定 Width 和 Height，由 UpdateLauncherTileLayout 动态设置
        };
        button.Click += (_, _) =>
        {
            if (_isComponentLibraryOpen)
            {
                if (!string.IsNullOrWhiteSpace(entryKey))
                {
                    SetSelectedLauncherTile(button, entryKind, entryKey);
                }

                return;
            }

            clickAction();
        };
        return button;
    }

    private static string NormalizeLauncherHiddenKey(string? key)
    {
        return string.IsNullOrWhiteSpace(key) ? string.Empty : key.Trim();
    }

    private bool IsLauncherFolderVisible(StartMenuFolderNode folder)
    {
        var key = NormalizeLauncherHiddenKey(folder.RelativePath);
        return string.IsNullOrWhiteSpace(key) || !_hiddenLauncherFolderPaths.Contains(key);
    }

    private bool IsLauncherAppVisible(StartMenuAppEntry app)
    {
        var key = NormalizeLauncherHiddenKey(app.RelativePath);
        return string.IsNullOrWhiteSpace(key) || !_hiddenLauncherAppPaths.Contains(key);
    }

    private bool IsLauncherTileSelected()
    {
        return _selectedLauncherEntryKind.HasValue && !string.IsNullOrWhiteSpace(_selectedLauncherEntryKey);
    }

    private void SetSelectedLauncherTile(Button button, LauncherEntryKind entryKind, string entryKey)
    {
        if (!_isComponentLibraryOpen || string.IsNullOrWhiteSpace(entryKey))
        {
            return;
        }

        var normalizedKey = NormalizeLauncherHiddenKey(entryKey);
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return;
        }

        if (_selectedDesktopComponentHost is not null)
        {
            ClearDesktopComponentSelection();
        }

        if (_selectedLauncherTileButton is not null && _selectedLauncherTileButton != button)
        {
            ApplyLauncherTileSelectionVisual(_selectedLauncherTileButton, isSelected: false);
        }

        _selectedLauncherTileButton = button;
        _selectedLauncherEntryKind = entryKind;
        _selectedLauncherEntryKey = normalizedKey;
        ApplyLauncherTileSelectionVisual(button, isSelected: true);
        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
    }

    private void ClearSelectedLauncherTile(bool refreshTaskbar)
    {
        if (_selectedLauncherTileButton is not null)
        {
            ApplyLauncherTileSelectionVisual(_selectedLauncherTileButton, isSelected: false);
        }

        _selectedLauncherTileButton = null;
        _selectedLauncherEntryKind = null;
        _selectedLauncherEntryKey = null;

        if (refreshTaskbar)
        {
            ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
        }
    }

    private void ApplyLauncherTileSelectionVisual(Button button, bool isSelected)
    {
        var showSelection = isSelected && _isComponentLibraryOpen;
        button.BorderThickness = showSelection
            ? new Thickness(Math.Clamp(_currentDesktopCellSize * 0.04, 1, 3))
            : new Thickness(0);
        button.BorderBrush = showSelection ? GetThemeBrush("AdaptiveAccentBrush") : Brushes.Transparent;
    }

    private void HideSelectedLauncherEntry()
    {
        if (!_isComponentLibraryOpen ||
            _currentDesktopSurfaceIndex != LauncherSurfaceIndex ||
            _selectedLauncherEntryKind is null ||
            string.IsNullOrWhiteSpace(_selectedLauncherEntryKey))
        {
            return;
        }

        var entryKind = _selectedLauncherEntryKind.Value;
        var entryKey = _selectedLauncherEntryKey!;
        ClearSelectedLauncherTile(refreshTaskbar: false);

        var changed = entryKind switch
        {
            LauncherEntryKind.Folder => _hiddenLauncherFolderPaths.Add(entryKey),
            LauncherEntryKind.Shortcut => _hiddenLauncherAppPaths.Add(entryKey),
            _ => false
        };

        if (changed)
        {
            ApplyLauncherVisibilitySettingsChange();
            return;
        }

        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
    }

    private void ApplyLauncherVisibilitySettingsChange()
    {
        ClearSelectedLauncherTile(refreshTaskbar: false);
        RenderLauncherRootTiles();
        if (_launcherFolderStack.Count > 0)
        {
            RenderLauncherFolderFromStack();
        }

        RenderLauncherHiddenItemsList();
        PersistSettings();
    }

    private void RenderLauncherHiddenItemsList()
    {
        if (LauncherHiddenItemsSettingsExpander is null || LauncherHiddenItemsEmptyTextBlock is null)
        {
            return;
        }

        LauncherHiddenItemsSettingsExpander.Items.Clear();
        var hiddenItems = BuildLauncherHiddenItems();
        LauncherHiddenItemsEmptyTextBlock.IsVisible = hiddenItems.Count == 0;
        if (hiddenItems.Count == 0)
        {
            return;
        }

        foreach (var hiddenItem in hiddenItems)
        {
            LauncherHiddenItemsSettingsExpander.Items.Add(CreateLauncherHiddenItemRow(hiddenItem));
        }
    }

    private IReadOnlyList<LauncherHiddenItemView> BuildLauncherHiddenItems()
    {
        var items = new List<LauncherHiddenItemView>();
        var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        CollectHiddenLauncherItems(_startMenuRoot, items, seenFolders, seenApps);

        foreach (var key in _hiddenLauncherFolderPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (!seenFolders.Contains(key))
            {
                items.Add(new LauncherHiddenItemView(
                    LauncherEntryKind.Folder,
                    key,
                    BuildLauncherHiddenFallbackDisplayName(key),
                    "DIR",
                    GetLauncherFolderIconBitmap()));
            }
        }

        foreach (var key in _hiddenLauncherAppPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (!seenApps.Contains(key))
            {
                var fallbackName = BuildLauncherHiddenFallbackDisplayName(key);
                items.Add(new LauncherHiddenItemView(
                    LauncherEntryKind.Shortcut,
                    key,
                    fallbackName,
                    BuildMonogram(fallbackName),
                    IconBitmap: null));
            }
        }

        return items
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void CollectHiddenLauncherItems(
        StartMenuFolderNode folder,
        List<LauncherHiddenItemView> items,
        HashSet<string> seenFolders,
        HashSet<string> seenApps)
    {
        foreach (var subFolder in folder.Folders)
        {
            var folderKey = NormalizeLauncherHiddenKey(subFolder.RelativePath);
            if (!string.IsNullOrWhiteSpace(folderKey) &&
                _hiddenLauncherFolderPaths.Contains(folderKey) &&
                seenFolders.Add(folderKey))
            {
                items.Add(new LauncherHiddenItemView(
                    LauncherEntryKind.Folder,
                    folderKey,
                    subFolder.Name,
                    "DIR",
                    GetLauncherFolderIconBitmap()));
            }

            CollectHiddenLauncherItems(subFolder, items, seenFolders, seenApps);
        }

        foreach (var app in folder.Apps)
        {
            var appKey = NormalizeLauncherHiddenKey(app.RelativePath);
            if (string.IsNullOrWhiteSpace(appKey) ||
                !_hiddenLauncherAppPaths.Contains(appKey) ||
                !seenApps.Add(appKey))
            {
                continue;
            }

            items.Add(new LauncherHiddenItemView(
                LauncherEntryKind.Shortcut,
                appKey,
                app.DisplayName,
                BuildMonogram(app.DisplayName),
                GetLauncherIconBitmap(app)));
        }
    }

    private static string BuildLauncherHiddenFallbackDisplayName(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "Unknown";
        }

        var normalized = key.Replace('\\', '/');
        var fileName = Path.GetFileNameWithoutExtension(normalized);
        return string.IsNullOrWhiteSpace(fileName)
            ? key
            : fileName;
    }

    private SettingsExpanderItem CreateLauncherHiddenItemRow(LauncherHiddenItemView hiddenItem)
    {
        var typeText = hiddenItem.Kind == LauncherEntryKind.Folder
            ? L("settings.launcher.hidden_type_folder", "Folder")
            : L("settings.launcher.hidden_type_shortcut", "Shortcut");

        var restoreButton = new Button
        {
            Width = 36,
            Height = 36,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Tag = new LauncherHiddenItemToken(hiddenItem.Kind, hiddenItem.Key)
        };
        restoreButton.Content = new FluentIcons.Avalonia.Fluent.SymbolIcon
        {
            Symbol = FluentIcons.Common.Symbol.Eye,
            IconVariant = FluentIcons.Common.IconVariant.Regular,
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(restoreButton, L("settings.launcher.restore_button", "Unhide"));
        restoreButton.Click += OnRestoreLauncherHiddenItemClick;

        return new SettingsExpanderItem
        {
            Content = hiddenItem.DisplayName,
            Description = typeText,
            IconSource = CreateLauncherHiddenItemIconSource(hiddenItem),
            IsClickEnabled = false,
            Footer = restoreButton
        };
    }

    private IconSource CreateLauncherHiddenItemIconSource(LauncherHiddenItemView hiddenItem)
    {
        if (hiddenItem.IconBitmap is not null)
        {
            return new ImageIconSource
            {
                Source = hiddenItem.IconBitmap
            };
        }

        return new FluentIcons.Avalonia.Fluent.SymbolIconSource
        {
            Symbol = hiddenItem.Kind == LauncherEntryKind.Folder
                ? FluentIcons.Common.Symbol.Folder
                : FluentIcons.Common.Symbol.Apps,
            IconVariant = FluentIcons.Common.IconVariant.Regular
        };
    }

    private void OnRestoreLauncherHiddenItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LauncherHiddenItemToken token })
        {
            return;
        }

        var removed = token.Kind switch
        {
            LauncherEntryKind.Folder => _hiddenLauncherFolderPaths.Remove(token.Key),
            LauncherEntryKind.Shortcut => _hiddenLauncherAppPaths.Remove(token.Key),
            _ => false
        };

        if (!removed)
        {
            return;
        }

        ApplyLauncherVisibilitySettingsChange();
    }

    private Bitmap? GetLauncherIconBitmap(StartMenuAppEntry app)
    {
        if (app.IconPngBytes is null || app.IconPngBytes.Length == 0)
        {
            return null;
        }

        if (_launcherIconCache.TryGetValue(app.RelativePath, out var cached))
        {
            return cached;
        }

        try
        {
            using var stream = new MemoryStream(app.IconPngBytes, writable: false);
            var bitmap = new Bitmap(stream);
            _launcherIconCache[app.RelativePath] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private Bitmap? GetLauncherFolderIconBitmap()
    {
        if (_launcherFolderIconBitmap is not null)
        {
            return _launcherFolderIconBitmap;
        }

        if (_launcherFolderIconPngBytes is null || _launcherFolderIconPngBytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(_launcherFolderIconPngBytes, writable: false);
            _launcherFolderIconBitmap = new Bitmap(stream);
            return _launcherFolderIconBitmap;
        }
        catch
        {
            _launcherFolderIconBitmap = null;
            return null;
        }
    }

    private void OpenLauncherFolder(StartMenuFolderNode folder)
    {
        _launcherFolderStack.Push(folder);
        RenderLauncherFolderFromStack();
    }

    private void CloseLauncherFolderOverlay()
    {
        ClearSelectedLauncherTile(refreshTaskbar: false);
        _launcherFolderStack.Clear();
        if (LauncherFolderOverlay is not null)
        {
            LauncherFolderOverlay.IsVisible = false;
        }

        if (LauncherFolderTilePanel is not null)
        {
            LauncherFolderTilePanel.Children.Clear();
        }
    }

    private void RenderLauncherFolderFromStack()
    {
        if (LauncherFolderOverlay is null ||
            LauncherFolderTilePanel is null ||
            LauncherFolderTitleTextBlock is null ||
            LauncherFolderBackButton is null)
        {
            return;
        }

        ClearSelectedLauncherTile(refreshTaskbar: false);
        if (_launcherFolderStack.Count == 0)
        {
            CloseLauncherFolderOverlay();
            return;
        }

        var folder = _launcherFolderStack.Peek();
        LauncherFolderOverlay.IsVisible = true;
        LauncherFolderTitleTextBlock.Text = folder.Name;
        LauncherFolderBackButton.IsVisible = _launcherFolderStack.Count > 1;

        LauncherFolderTilePanel.Children.Clear();
        foreach (var subFolder in folder.Folders)
        {
            if (!IsLauncherFolderVisible(subFolder))
            {
                continue;
            }

            LauncherFolderTilePanel.Children.Add(CreateLauncherFolderTile(subFolder));
        }

        foreach (var app in folder.Apps)
        {
            if (!IsLauncherAppVisible(app))
            {
                continue;
            }

            LauncherFolderTilePanel.Children.Add(CreateLauncherAppTile(app));
        }

        if (LauncherFolderTilePanel.Children.Count == 0)
        {
            LauncherFolderTilePanel.Children.Add(CreateLauncherHintTile(
                L("launcher.empty_folder", "This folder is empty."),
                string.Empty));
        }

        // 在图标渲染完成后，应用布局计算
        Dispatcher.UIThread.Post(() => UpdateLauncherTileLayout(), DispatcherPriority.Background);
    }

    private static string BuildMonogram(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "?";
        }

        var letters = text
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part[0])
            .Take(2)
            .ToArray();
        if (letters.Length == 0)
        {
            return "?";
        }

        return new string(letters).ToUpperInvariant();
    }

    private string GetLauncherEmptyText()
    {
        return OperatingSystem.IsLinux()
            ? L("launcher.empty_linux", "No Linux desktop entries were found.")
            : L("launcher.empty", "No Start Menu entries found.");
    }

    private static void LaunchStartMenuEntry(StartMenuAppEntry app)
    {
        try
        {
            if (OperatingSystem.IsLinux() &&
                !string.IsNullOrWhiteSpace(app.LaunchExecutable))
            {
                var linuxStartInfo = new ProcessStartInfo
                {
                    FileName = app.LaunchExecutable,
                    UseShellExecute = false
                };

                if (!string.IsNullOrWhiteSpace(app.WorkingDirectory))
                {
                    linuxStartInfo.WorkingDirectory = app.WorkingDirectory;
                }

                foreach (var argument in app.LaunchArguments)
                {
                    linuxStartInfo.ArgumentList.Add(argument);
                }

                Process.Start(linuxStartInfo);
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = app.FilePath,
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }
        catch
        {
            // Ignore failures to launch malformed shortcuts.
        }
    }

    private void OnLauncherFolderBackClick(object? sender, RoutedEventArgs e)
    {
        if (_launcherFolderStack.Count <= 1)
        {
            CloseLauncherFolderOverlay();
            return;
        }

        _launcherFolderStack.Pop();
        RenderLauncherFolderFromStack();
    }

    private void OnLauncherFolderOverlayPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (LauncherFolderPanel is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(LauncherFolderPanel).Position;
        if (point.X >= 0 &&
            point.Y >= 0 &&
            point.X <= LauncherFolderPanel.Bounds.Width &&
            point.Y <= LauncherFolderPanel.Bounds.Height)
        {
            return;
        }

        CloseLauncherFolderOverlay();
        e.Handled = true;
    }

    private void OnLauncherFolderCloseClick(object? sender, RoutedEventArgs e)
    {
        CloseLauncherFolderOverlay();
    }

    private void DisposeLauncherResources()
    {
        foreach (var bitmap in _launcherIconCache.Values)
        {
            bitmap.Dispose();
        }

        _launcherIconCache.Clear();
        _launcherFolderIconBitmap?.Dispose();
        _launcherFolderIconBitmap = null;
    }
}
