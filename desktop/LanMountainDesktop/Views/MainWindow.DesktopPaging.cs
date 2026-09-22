using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
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
using LanMountainDesktop.Platform.Windows;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;
using LanMountainDesktop.Views.Components;

namespace LanMountainDesktop.Views;

public partial class MainWindow : Window
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
    private bool _showLauncherTileBackground = true;
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
    private Transitions? _desktopPagesHostSnapTransitions;
    private bool _desktopPagesHostTransitionsSuspended;
    private bool _isDesktopSwipeActive;
    private bool _isDesktopSwipeDirectionLocked;
    private Point _desktopSwipeStartPoint;
    private Point _desktopSwipeCurrentPoint;
    private Point _desktopSwipeLastPoint;
    private long _desktopSwipeLastTimestamp;
    private double _desktopSwipeVelocityX;
    private double _desktopSwipeBaseOffset;
    private int? _desktopSwipePointerId;
    private bool _desktopPageContextInitialized;
    private bool _desktopPageContextEditMode;
    private int _desktopPageContextActiveMask;
    private int? _desktopPageContextSettlingSourceIndex;
    private int? _desktopPageContextSettlingTargetIndex;
    private int _desktopPageContextSettleRevision;

    // 婵犵數鍋為崹鍫曞箰閹间絸鍥箥椤旂懓浜鹃柛顭戝亯婢规ɑ銇勯婊冨妤犵偛顑呴埞鎴﹀窗?闂傚倷绀侀幉锟犳偡閿旂晫绠惧┑鐘叉搐閺嬩焦銇勯幘鍗炵仼缂佺媭鍨堕弻鈥崇暤椤旂厧鏁俊銈呮噺閻撶喖鏌嶉崫鍕灓闁绘帡绠栭弻?
    private bool _isThreeFingerOrRightDragSwipeActive;
    private readonly HashSet<int> _activePointerIds = [];

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

        _showLauncherTileBackground = snapshot.ShowTileBackground;
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

        if (_desktopPagesHostTransitionsSuspended)
        {
            _desktopPagesHostTransform.Transitions = null;
        }
        else
        {
            _desktopPagesHostSnapTransitions ??= _desktopPagesHostTransform.Transitions;
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
            UpdateDesktopEditOverlayViewportSize();
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
        InvalidateDesktopPageAwareComponentContextCache();
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

        var launcherMargin = Math.Clamp(gridMetrics.CellSize * 0.15, 6, 16);
        LauncherPagePanel.Margin = new Thickness(launcherMargin);
        LauncherPagePanel.Width = Math.Max(1, pageWidth - launcherMargin * 2);
        LauncherPagePanel.Height = Math.Max(1, pageHeight - launcherMargin * 2);
        LauncherPagePanel.MaxWidth = pageWidth - launcherMargin * 2;
        LauncherPagePanel.MaxHeight = pageHeight - launcherMargin * 2;

        // 闂傚倷绀侀幖顐⒚洪妶澶嬪仱闁靛ň鏅涢拑鐔封攽閻樺弶鎼愰悷娆欓檮閵囧嫰寮介妸銊ヮ棟閻炴氨鍠栧娲川婵犲嫭鍣┑鐘灪閿氶棁澶嬫叏濡炶浜鹃悗娈垮枙缁瑥鐣烽幆閭︽Ь濡炪倕绻戦幐鎶藉箖濮椻偓閹瑩鍩℃担宄邦棜
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

        var availableWidth = Math.Max(1, LauncherPagePanel.Bounds.Width - 36); // 18px padding on each side
        var availableHeight = Math.Max(1, LauncherPagePanel.Bounds.Height - 100); // 上下各预留 50px 给标题与分页指示

        if (availableWidth <= 1 || availableHeight <= 1)
        {
            // 面板尚未完成布局时退回一个可用尺寸
            availableWidth = 600;
            availableHeight = 400;
        }

        // 列数按可用宽度推导，最少 4 列、最多 8 列
        const int minColumns = 4;
        const int maxColumns = 8;
        const double targetAspectRatio = 1.2; // 目标宽高比 1.2（宽:高）
        var optimalColumnCount = Math.Clamp((int)Math.Floor(availableWidth / 120), minColumns, maxColumns);

        var tileWidth = Math.Floor(availableWidth / optimalColumnCount) - 12; // 12px spacing
        var tileHeight = Math.Min(tileWidth / targetAspectRatio, availableHeight / 4); // 高度同时受宽高比与首屏行数限制
        // 夹住最小尺寸，避免窄面板下 tile 塌陷
        tileWidth = Math.Max(tileWidth, 100);
        tileHeight = Math.Max(tileHeight, 80);

        // 面板宽度跟随可用宽度，子项才能按列数正确换行
        LauncherRootTilePanel.Width = availableWidth;

        // 把推导出的尺寸应用到每个 tile
        foreach (var child in LauncherRootTilePanel.Children)
        {
            if (child is Button button)
            {
                button.Width = tileWidth;
                button.Height = tileHeight;
            }
        }

    }

    private void ClampSurfaceIndex()
    {
        _currentDesktopSurfaceIndex = Math.Clamp(_currentDesktopSurfaceIndex, 0, LauncherSurfaceIndex);
    }

    private IBrush GetThemeBrush(string key) => AdaptiveTokens.Brush(this, key, Brushes.Transparent);

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

    private void SetDesktopPagesHostSnapAnimationEnabled(bool enabled)
    {
        if (_desktopPagesHostTransform is null)
        {
            return;
        }

        if (enabled)
        {
            if (!_desktopPagesHostTransitionsSuspended)
            {
                return;
            }

            _desktopPagesHostTransform.Transitions = _desktopPagesHostSnapTransitions;
            _desktopPagesHostTransitionsSuspended = false;
            return;
        }

        if (_desktopPagesHostTransitionsSuspended)
        {
            return;
        }

        _desktopPagesHostSnapTransitions ??= _desktopPagesHostTransform.Transitions;
        _desktopPagesHostTransform.Transitions = null;
        _desktopPagesHostTransitionsSuspended = true;
    }

    private void ClearDesktopPageContextSettle(bool refreshContext)
    {
        _desktopPageContextSettleRevision++;
        _desktopPageContextSettlingSourceIndex = null;
        _desktopPageContextSettlingTargetIndex = null;

        if (refreshContext)
        {
            UpdateDesktopPageAwareComponentContext();
        }
    }

    private void BeginDesktopPageContextSettle(int previousIndex, int targetIndex)
    {
        var sourceIndex = previousIndex >= 0 && previousIndex < _desktopPageCount
            ? previousIndex
            : (int?)null;
        var destinationIndex = targetIndex >= 0 && targetIndex < _desktopPageCount
            ? targetIndex
            : (int?)null;

        if (sourceIndex == destinationIndex && destinationIndex is not null)
        {
            ClearDesktopPageContextSettle(refreshContext: false);
            return;
        }

        if (sourceIndex is null && destinationIndex is null)
        {
            ClearDesktopPageContextSettle(refreshContext: false);
            return;
        }

        _desktopPageContextSettleRevision++;
        var settleRevision = _desktopPageContextSettleRevision;
        _desktopPageContextSettlingSourceIndex = sourceIndex;
        _desktopPageContextSettlingTargetIndex = destinationIndex;

        DispatcherTimer.RunOnce(
            () =>
            {
                if (settleRevision != _desktopPageContextSettleRevision)
                {
                    return;
                }

                _desktopPageContextSettlingSourceIndex = null;
                _desktopPageContextSettlingTargetIndex = null;
                UpdateDesktopPageAwareComponentContext();
            },
            FluttermotionToken.Page + TimeSpan.FromMilliseconds(36));
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

        var previousIndex = _currentDesktopSurfaceIndex;
        _currentDesktopSurfaceIndex = target;
        BeginDesktopPageContextSettle(previousIndex, target);
        ApplyDesktopSurfaceOffset();
        SchedulePersistSettings(delayMs: Math.Max(280, (int)FluttermotionToken.Page.TotalMilliseconds + 80));
    }

    private bool CanSwipeDesktopSurface()
    {
        return !_isSettingsOpen &&
               !_isComponentLibraryOpen &&
               !HasActiveDesktopEditSession &&
               _desktopSurfacePageWidth > 1;
    }

    private void OnDesktopPagesPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!TryGetPointerPositionInDesktopViewport(e, out var pointerInViewport))
        {
            return;
        }

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

        var appSnapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(AirAppSettingsScope.App);
        var isThreeFingerSwipeEnabled = appSnapshot.EnableThreeFingerSwipe;

        var currentPoint = e.GetCurrentPoint(DesktopPagesViewport);
        var pointerId = e.Pointer?.Id ?? 0;
        var isRightButtonPressed = currentPoint.Properties.IsRightButtonPressed;
        var isLeftButtonPressed = currentPoint.Properties.IsLeftButtonPressed;

        // 婵犵數濮伴崹鐓庘枖濞戞埃鍋撳鐓庢珝妤犵偛鍟换婵嬪礃椤忎焦鐏冨┑鐘灱濞夋盯顢栭崨瀛樺剨閻熸瑥瀚弧鈧繝鐢靛Т閸燁偊鎮橀妷銉㈡斀?闂傚倷绀侀幉锟犳偡閿旂晫绠惧┑鐘叉搐閺嬩焦銇勯幘鍗炵仼缂佺媭鍨堕弻鈥崇暤椤旂厧鏁俊銈勬缁诲棙銇勯弽銊ｄ粶闁稿鎸搁悾鐑藉炊閳哄﹥鏁?
        if (isThreeFingerSwipeEnabled)
        {
            if (isLeftButtonPressed || isRightButtonPressed)
            {
                _activePointerIds.Add(pointerId);
            }

            var isThreeFinger = _activePointerIds.Count >= 3;
            var isRightDrag = isRightButtonPressed;

            if (isThreeFinger || isRightDrag)
            {
                ClearDesktopPageContextSettle(refreshContext: false);
                _isThreeFingerOrRightDragSwipeActive = true;
                _isDesktopSwipeActive = true;
                _isDesktopSwipeDirectionLocked = false;
                _desktopSwipeStartPoint = pointerInViewport;
                _desktopSwipeCurrentPoint = _desktopSwipeStartPoint;
                _desktopSwipeLastPoint = _desktopSwipeStartPoint;
                _desktopSwipeVelocityX = 0;
                _desktopSwipeLastTimestamp = Stopwatch.GetTimestamp();
                _desktopSwipeBaseOffset = -_currentDesktopSurfaceIndex * _desktopSurfacePageWidth;
                _desktopSwipePointerId = pointerId;
                e.Handled = true;
                
                return;
            }
        }

        if (IsInteractivePointerSource(e.Source))
        {
            return;
        }

        if (IsDesktopSwipeBlockedPointerSource(e.Source))
        {
            return;
        }

        if (!isLeftButtonPressed)
        {
            return;
        }

        ClearDesktopPageContextSettle(refreshContext: false);
        _isDesktopSwipeActive = true;
        _isDesktopSwipeDirectionLocked = false;
        _desktopSwipeStartPoint = pointerInViewport;
        _desktopSwipeCurrentPoint = _desktopSwipeStartPoint;
        _desktopSwipeLastPoint = _desktopSwipeStartPoint;
        _desktopSwipeVelocityX = 0;
        _desktopSwipeLastTimestamp = Stopwatch.GetTimestamp();
        _desktopSwipeBaseOffset = -_currentDesktopSurfaceIndex * _desktopSurfacePageWidth;
        _desktopSwipePointerId = pointerId;
    }

    private bool IsDesktopSwipePointer(IPointer? pointer)
    {
        return !_desktopSwipePointerId.HasValue ||
               pointer is not null && pointer.Id == _desktopSwipePointerId.Value;
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
                if (control.Classes.Contains("desktop-component") ||
                    control.Classes.Contains("desktop-component-host"))
                {
                    return true;
                }
            }

            if (node is Button button && IsLauncherTileButton(button))
            {
                continue;
            }

            if (node is TextBox or ComboBox or ListBoxItem or Slider or ToggleSwitch)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLauncherTileButton(Button? button)
    {
        if (button is null)
        {
            return false;
        }

        foreach (var node in button.GetSelfAndVisualAncestors())
        {
            if (node is WrapPanel panel && panel.Name == "LauncherRootTilePanel")
            {
                return true;
            }

            if (node is Grid grid && grid.Name == "LauncherFolderGridPanel")
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
        if (node is ScrollViewer scrollViewer && IsLauncherScrollViewer(scrollViewer))
        {
            return false;
        }

        if (node is Button button && IsLauncherTileButton(button))
        {
            return false;
        }

        if (node is TextBox or ComboBox or Slider or ToggleSwitch or ListBoxItem)
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
        return typeName.Contains("WebView", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("ScrollBar", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("NumericUpDown", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("TextPresenter", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLauncherScrollViewer(ScrollViewer? scrollViewer)
    {
        if (scrollViewer is null)
        {
            return false;
        }

        return scrollViewer.Name == "LauncherRootScrollViewer";
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
        if (_isDesktopSwipeActive && !IsDesktopSwipePointer(e.Pointer))
        {
            return;
        }

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
            SetDesktopPagesHostSnapAnimationEnabled(enabled: false);
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
        UpdateDesktopPageAwareComponentContext();
        e.Handled = true;
    }

    private void OnDesktopPagesPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var pointerId = e.Pointer?.Id ?? 0;
        _activePointerIds.Remove(pointerId);

        if (_isDesktopSwipeActive && !IsDesktopSwipePointer(e.Pointer))
        {
            return;
        }
        
        if (EndDesktopSwipeInteraction(e.Pointer))
        {
            e.Handled = true;
        }
    }

    private void OnDesktopPagesPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        var pointerId = e.Pointer?.Id ?? 0;
        _activePointerIds.Remove(pointerId);

        if (!_isDesktopSwipeActive || !IsDesktopSwipePointer(e.Pointer))
        {
            return;
        }

        if (e.Pointer?.Captured == DesktopPagesViewport)
        {
            return;
        }

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
        _isThreeFingerOrRightDragSwipeActive = false;
        _activePointerIds.Clear();
        _desktopSwipePointerId = null;
        _desktopSwipeVelocityX = 0;
        _desktopSwipeLastTimestamp = 0;
        if (wasDirectionLocked)
        {
            SetDesktopPagesHostSnapAnimationEnabled(enabled: true);
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
        var wasThreeFingerOrRightDrag = _isThreeFingerOrRightDragSwipeActive;
        _isDesktopSwipeActive = false;
        _isDesktopSwipeDirectionLocked = false;
        _isThreeFingerOrRightDragSwipeActive = false;
        _activePointerIds.Clear();
        _desktopSwipePointerId = null;
        
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

        SetDesktopPagesHostSnapAnimationEnabled(enabled: true);

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

        // 濠电姷顣藉Σ鍛村磻閳ь剟鏌涚€ｎ偅宕岄柡宀嬬磿娴狅妇鎷犻幓鎺懶ョ紓鍌欐祰娴滎剚鏅跺Δ鍐煓濠㈣泛顑呯欢鐐烘倵閿濆簼绨芥俊?闂傚倷绀侀幉锟犳偡閿旂晫绠惧┑鐘叉搐閺嬩焦銇勯幘鍗炵仼缂佺媭鍨堕弻鈥崇暤椤旂厧鏁?&& 闂傚倷绶氬鑽ゆ嫻閻旂厧绀夐幖鎼厛閺佸嫰鏌涢妷锝呭闁崇粯妫冮弻宥堫檨闁告挻宀告俊?&& 闂傚倷绀侀幉锛勫枈瀹ュ鍨傚ù锝呭暔娴滃湱绱掔€ｎ偒鍎ラ柣鎾卞劦閺岀喓鈧稒顭囩粻鎾舵偖?
        if (wasThreeFingerOrRightDrag && 
            _currentDesktopSurfaceIndex == 0 && 
            deltaX > 0 && // 闂傚倷绀侀幉锛勫枈瀹ュ鍨傚ù锝呭暔娴滃湱绱掔€ｎ偒鍎ラ柣鎾卞劦閺岀喓鈧稒顭囩粻鎾舵偖?
            (hasDistanceIntent || hasVelocityIntent))
        {
            if (Application.Current is App app)
            {
                app.HideMainWindowToTray(this, "ThreeFingerOrRightDragSwipe");
            }
            
            ApplyDesktopSurfaceOffset();
            _desktopSwipeVelocityX = 0;
            return true;
        }

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

        // 瓦片重建完成后补一次布局刷新，避免首帧按旧尺寸排布
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
        var monogram = Monogram.From(app.DisplayName);
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
            Classes = { ComponentChromePanel.GlassPanelClass },
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 12, 12),
            CornerRadius = new CornerRadius(20),
            Child = panel,
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
                Background = GetThemeBrush(ThemeResourceKeys.ButtonBackgroundBrush),
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
            Margin = new Thickness(0, 0, 12, 12),
            BorderThickness = new Thickness(0),
            BorderBrush = Brushes.Transparent,
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(10),
            Content = content,
        };
        if (_showLauncherTileBackground)
        {
            button.Classes.Add(ComponentChromePanel.GlassPanelClass);
        }
        else
        {
            button.Background = Brushes.Transparent;
        }
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
        button.BorderBrush = showSelection ? GetThemeBrush(ThemeResourceKeys.AccentBrush) : Brushes.Transparent;
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
                    Monogram.From(fallbackName),
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
                Monogram.From(app.DisplayName),
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

    private FASettingsExpanderItem CreateLauncherHiddenItemRow(LauncherHiddenItemView hiddenItem)
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
        restoreButton.Content = new FluentIcons.Avalonia.SymbolIcon
        {
            Symbol = FluentIcons.Common.Symbol.Eye,
            IconVariant = FluentIcons.Common.IconVariant.Regular,
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(restoreButton, L("settings.launcher.restore_button", "Unhide"));
        restoreButton.Click += OnRestoreLauncherHiddenItemClick;

        return new FASettingsExpanderItem
        {
            Content = hiddenItem.DisplayName,
            Description = typeText,
            IconSource = CreateLauncherHiddenItemIconSource(hiddenItem),
            IsClickEnabled = false,
            Footer = restoreButton
        };
    }

    private FAIconSource? CreateLauncherHiddenItemIconSource(LauncherHiddenItemView hiddenItem)
    {
        if (hiddenItem.IconBitmap is not null)
        {
            return new FAImageIconSource
            {
                Source = hiddenItem.IconBitmap
            };
        }

        return null;
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

        if (LauncherFolderGridPanel is not null)
        {
            LauncherFolderGridPanel.Children.Clear();
        }
    }

    private void RenderLauncherFolderFromStack()
    {
        if (LauncherFolderOverlay is null ||
            LauncherFolderGridPanel is null ||
            LauncherFolderTitleTextBlock is null)
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

        LauncherFolderGridPanel.Children.Clear();

        const int maxCols = 4;
        const int maxRows = 3;
        const int maxItems = maxCols * maxRows;

        var visibleFolders = folder.Folders.Where(IsLauncherFolderVisible).ToList();
        var visibleApps = folder.Apps.Where(IsLauncherAppVisible).ToList();

        if (visibleFolders.Count == 0 && visibleApps.Count == 0)
        {
            LauncherFolderGridPanel.Children.Add(CreateLauncherFolderGridHintCell(
                L("launcher.empty_folder", "This folder is empty.")));
            return;
        }

        var allItems = new List<(StartMenuFolderNode? Folder, StartMenuAppEntry? App)>();
        foreach (var f in visibleFolders)
        {
            allItems.Add((f, null));
        }
        foreach (var a in visibleApps)
        {
            allItems.Add((null, a));
        }

        var displayCount = Math.Min(allItems.Count, maxItems);
        for (var i = 0; i < displayCount; i++)
        {
            var col = i % maxCols;
            var row = i / maxCols;
            var (itemFolder, itemApp) = allItems[i];

            Control cell;
            if (itemFolder is not null)
            {
                var capturedFolder = itemFolder;
                cell = CreateLauncherFolderGridTile(itemFolder.Name, GetLauncherFolderIconBitmap(), () => OpenLauncherFolder(capturedFolder));
            }
            else if (itemApp is not null)
            {
                var capturedApp = itemApp;
                cell = CreateLauncherFolderGridTile(capturedApp, () => LaunchStartMenuEntry(capturedApp));
            }
            else
            {
                continue;
            }

            Grid.SetColumn(cell, col);
            Grid.SetRow(cell, row);
            LauncherFolderGridPanel.Children.Add(cell);
        }
    }

    private Button CreateLauncherFolderGridTile(StartMenuAppEntry app, Action clickAction)
    {
        var iconBitmap = GetLauncherIconBitmap(app);
        var monogram = Monogram.From(app.DisplayName);

        Control iconControl = iconBitmap is not null
            ? new Image
            {
                Source = iconBitmap,
                Width = 32,
                Height = 32,
                Stretch = Stretch.Uniform
            }
            : new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(8),
                Background = GetThemeBrush(ThemeResourceKeys.ButtonBackgroundBrush),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = monogram,
                    FontSize = 13,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

        var content = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(iconControl);
        content.Children.Add(new TextBlock
        {
            Text = app.DisplayName,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
            TextAlignment = TextAlignment.Center,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch
        });

        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 8, 8, 6),
            Content = content
        };

        if (_showLauncherTileBackground)
        {
            button.Classes.Add(ComponentChromePanel.GlassPanelClass);
        }
        else
        {
            button.Background = Brushes.Transparent;
        }

        button.Click += (_, _) =>
        {
            if (_isComponentLibraryOpen)
            {
                return;
            }

            clickAction();
        };
        return button;
    }

    private Button CreateLauncherFolderGridTile(string folderName, Bitmap? iconBitmap, Action clickAction)
    {
        var monogram = "DIR";

        Control iconControl = iconBitmap is not null
            ? new Image
            {
                Source = iconBitmap,
                Width = 32,
                Height = 32,
                Stretch = Stretch.Uniform
            }
            : new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(8),
                Background = GetThemeBrush(ThemeResourceKeys.ButtonBackgroundBrush),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = monogram,
                    FontSize = 11,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

        var content = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(iconControl);
        content.Children.Add(new TextBlock
        {
            Text = folderName,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
            TextAlignment = TextAlignment.Center,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch
        });

        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 8, 8, 6),
            Content = content
        };

        if (_showLauncherTileBackground)
        {
            button.Classes.Add(ComponentChromePanel.GlassPanelClass);
        }
        else
        {
            button.Background = Brushes.Transparent;
        }

        button.Click += (_, _) =>
        {
            if (_isComponentLibraryOpen)
            {
                return;
            }

            clickAction();
        };
        return button;
    }

    private Control CreateLauncherFolderGridHintCell(string message)
    {
        return CreateLauncherFolderGridHintCell(message, 0, 0);
    }

    private Control CreateLauncherFolderGridHintCell(string message, int col, int row)
    {
        var textBlock = new TextBlock
        {
            Text = message,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.6
        };

        var cell = new Border
        {
            Classes = { ComponentChromePanel.GlassPanelClass },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            CornerRadius = new CornerRadius(12),
            Child = textBlock
        };

        Grid.SetColumn(cell, col);
        Grid.SetRow(cell, row);
        return cell;
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
