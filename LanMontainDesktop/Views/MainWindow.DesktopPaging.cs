using System;
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
using LanMontainDesktop.Models;
using LanMontainDesktop.Services;

namespace LanMontainDesktop.Views;

public partial class MainWindow
{
    private const int MinDesktopPageCount = 1;
    private const int MaxDesktopPageCount = 12;
    private readonly WindowsStartMenuService _windowsStartMenuService = new();
    private readonly Dictionary<string, Bitmap> _launcherIconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<StartMenuFolderNode> _launcherFolderStack = [];
    private StartMenuFolderNode _startMenuRoot = new("All Apps", string.Empty);
    private byte[]? _launcherFolderIconPngBytes;
    private Bitmap? _launcherFolderIconBitmap;
    private int _desktopPageCount = MinDesktopPageCount;
    private int _currentDesktopSurfaceIndex;
    private double _desktopSurfacePageWidth;
    private TranslateTransform? _desktopPagesHostTransform;
    private bool _isDesktopSwipeActive;
    private Point _desktopSwipeStartPoint;
    private Point _desktopSwipeCurrentPoint;
    private double _desktopSwipeBaseOffset;

    private int LauncherSurfaceIndex => Math.Max(MinDesktopPageCount, _desktopPageCount);

    private int TotalSurfaceCount => LauncherSurfaceIndex + 1;

    private void InitializeDesktopSurfaceState(AppSettingsSnapshot snapshot)
    {
        var loadedPageCount = snapshot.DesktopPageCount <= 0 ? MinDesktopPageCount : snapshot.DesktopPageCount;
        _desktopPageCount = Math.Clamp(loadedPageCount, MinDesktopPageCount, MaxDesktopPageCount);
        _currentDesktopSurfaceIndex = Math.Clamp(snapshot.CurrentDesktopSurfaceIndex, 0, LauncherSurfaceIndex);
    }

    private async void LoadLauncherEntriesAsync()
    {
        try
        {
            var loadResult = await Task.Run(() =>
            {
                var loadedRoot = _windowsStartMenuService.Load();
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
            }, DispatcherPriority.Background);
        }
        catch
        {
            _startMenuRoot = new StartMenuFolderNode("All Apps", string.Empty);
            _launcherFolderIconPngBytes = null;
            _launcherFolderIconBitmap?.Dispose();
            _launcherFolderIconBitmap = null;
            RenderLauncherRootTiles();
        }
    }

    private void UpdateDesktopSurfaceLayout(GridMetrics gridMetrics)
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
        var pageWidth = Math.Max(1, gridMetrics.ColumnCount * gridMetrics.CellSize);
        var pageHeight = Math.Max(1, viewportRowSpan * gridMetrics.CellSize);

        Grid.SetRow(DesktopPagesViewport, viewportRow);
        Grid.SetColumn(DesktopPagesViewport, 0);
        Grid.SetRowSpan(DesktopPagesViewport, viewportRowSpan);
        Grid.SetColumnSpan(DesktopPagesViewport, gridMetrics.ColumnCount);
        DesktopPagesViewport.Width = pageWidth;
        DesktopPagesViewport.Height = pageHeight;

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
        DesktopPagesContainer.Children.Clear();
        DesktopPagesContainer.Width = pageWidth * _desktopPageCount;
        DesktopPagesContainer.Height = pageHeight;
        for (var index = 0; index < _desktopPageCount; index++)
        {
            DesktopPagesContainer.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(pageWidth, GridUnitType.Pixel)));
            var pageSurface = new Border
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10)
            };

            if (_desktopPageCount > 1)
            {
                pageSurface.Child = new TextBlock
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Foreground = Foreground,
                    Opacity = 0.72,
                    Text = Lf("desktop.page_index_format", "Desktop {0}", index + 1)
                };
            }

            Grid.SetColumn(pageSurface, index);
            Grid.SetRow(pageSurface, 0);
            DesktopPagesContainer.Children.Add(pageSurface);
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
        }
    }

    private void MoveSurfaceBy(int delta)
    {
        if (delta == 0)
        {
            return;
        }

        var target = Math.Clamp(_currentDesktopSurfaceIndex + delta, 0, LauncherSurfaceIndex);
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
        return !_isSettingsOpen && !_isComponentLibraryOpen && _desktopSurfacePageWidth > 1;
    }

    private void OnDesktopPagesPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!CanSwipeDesktopSurface() || DesktopPagesViewport is null)
        {
            return;
        }

        if (IsInteractivePointerSource(e.Source))
        {
            return;
        }

        if (!e.GetCurrentPoint(DesktopPagesViewport).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isDesktopSwipeActive = true;
        _desktopSwipeStartPoint = e.GetPosition(DesktopPagesViewport);
        _desktopSwipeCurrentPoint = _desktopSwipeStartPoint;
        _desktopSwipeBaseOffset = -_currentDesktopSurfaceIndex * _desktopSurfacePageWidth;
        e.Pointer.Capture(DesktopPagesViewport);
    }

    private static bool IsInteractivePointerSource(object? source)
    {
        if (source is not Visual visual)
        {
            return false;
        }

        foreach (var node in visual.GetSelfAndVisualAncestors())
        {
            if (node is Button or TextBox or ComboBox or ListBoxItem or Slider or ToggleSwitch)
            {
                return true;
            }
        }

        return false;
    }

    private void OnDesktopPagesPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDesktopSwipeActive || DesktopPagesViewport is null || _desktopPagesHostTransform is null)
        {
            return;
        }

        _desktopSwipeCurrentPoint = e.GetPosition(DesktopPagesViewport);
        var deltaX = _desktopSwipeCurrentPoint.X - _desktopSwipeStartPoint.X;
        var minOffset = -LauncherSurfaceIndex * _desktopSurfacePageWidth;
        var tentative = _desktopSwipeBaseOffset + deltaX;
        _desktopPagesHostTransform.X = Math.Clamp(tentative, minOffset, 0);
        e.Handled = true;
    }

    private void OnDesktopPagesPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        EndDesktopSwipeInteraction(e.Pointer);
    }

    private void OnDesktopPagesPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndDesktopSwipeInteraction(e.Pointer);
    }

    private void EndDesktopSwipeInteraction(IPointer? pointer)
    {
        if (!_isDesktopSwipeActive)
        {
            return;
        }

        _isDesktopSwipeActive = false;
        if (pointer?.Captured == DesktopPagesViewport)
        {
            pointer.Capture(null);
        }

        var deltaX = _desktopSwipeCurrentPoint.X - _desktopSwipeStartPoint.X;
        var deltaY = _desktopSwipeCurrentPoint.Y - _desktopSwipeStartPoint.Y;
        var threshold = Math.Max(56, _desktopSurfacePageWidth * 0.16);
        if (Math.Abs(deltaX) >= threshold && Math.Abs(deltaX) > Math.Abs(deltaY))
        {
            MoveSurfaceBy(deltaX < 0 ? 1 : -1);
            return;
        }

        ApplyDesktopSurfaceOffset();
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

        LauncherRootTilePanel.Children.Clear();
        var folders = _startMenuRoot.Folders;
        var apps = _startMenuRoot.Apps;

        foreach (var folder in folders)
        {
            LauncherRootTilePanel.Children.Add(CreateLauncherFolderTile(folder));
        }

        foreach (var app in apps)
        {
            LauncherRootTilePanel.Children.Add(CreateLauncherAppTile(app));
        }

        if (LauncherRootTilePanel.Children.Count == 0)
        {
            LauncherRootTilePanel.Children.Add(CreateLauncherHintTile(
                L("launcher.empty", "No Start Menu entries found."),
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
        return CreateLauncherTileButton(
            title,
            subtitle,
            monogram: "DIR",
            iconBitmap: folderIconBitmap,
            () => OpenLauncherFolder(folder));
    }

    private Button CreateLauncherAppTile(StartMenuAppEntry app)
    {
        var iconBitmap = GetLauncherIconBitmap(app);
        var monogram = BuildMonogram(app.DisplayName);
        return CreateLauncherTileButton(
            app.DisplayName,
            subtitle: string.Empty,
            monogram,
            iconBitmap,
            () => LaunchStartMenuEntry(app));
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
            CornerRadius = new CornerRadius(12),
            Child = panel
            // 不设置固定 Width 和 Height，由 UpdateLauncherTileLayout 动态设置
        };
    }

    private Button CreateLauncherTileButton(
        string title,
        string subtitle,
        string monogram,
        Bitmap? iconBitmap,
        Action clickAction)
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
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10),
            Content = content
            // 不设置固定 Width 和 Height，由 UpdateLauncherTileLayout 动态设置
        };
        button.Click += (_, _) => clickAction();
        return button;
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
            LauncherFolderTilePanel.Children.Add(CreateLauncherFolderTile(subFolder));
        }

        foreach (var app in folder.Apps)
        {
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

    private static void LaunchStartMenuEntry(StartMenuAppEntry app)
    {
        try
        {
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
