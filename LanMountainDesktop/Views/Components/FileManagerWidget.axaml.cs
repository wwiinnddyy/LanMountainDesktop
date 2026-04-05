using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FluentIcons.Avalonia;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class FileManagerWidget : UserControl,
    IDesktopComponentWidget,
    IDesktopPageVisibilityAwareComponentWidget,
    IComponentPlacementContextAware,
    IDisposable
{
    private readonly List<string> _navigationHistory = new();
    private int _currentHistoryIndex = -1;
    private string _currentPath = string.Empty;
    private string _componentId = BuiltInComponentIds.DesktopFileManager;
    private string _placementId = string.Empty;
    private double _currentCellSize = 48;
    private bool _isOnActivePage;
    private bool _isEditMode;
    private bool _isAttached;
    private bool _isDisposed;

    private const double TapMovementThreshold = 10;
    private const long TapTimeThresholdMs = 500;

    private readonly Dictionary<int, PointerGestureState> _gestureStates = new();

    private record PointerGestureState(
        Point StartPosition,
        long StartTime,
        FileSystemItem Item,
        Border Border
    );

    public FileManagerWidget()
    {
        InitializeComponent();

        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;

        ApplyCellSize(_currentCellSize);
        NavigateToDrives();
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);

        var mainRectangleCornerRadius = ComponentChromeCornerRadiusHelper.ResolveMainRectangleRadius();
        RootBorder.CornerRadius = mainRectangleCornerRadius;
        RootBorder.Padding = new Thickness(
            Math.Clamp(_currentCellSize * 0.25, 10, 20),
            Math.Clamp(_currentCellSize * 0.20, 8, 16));

        ApplyLayoutMetrics();
    }

    public void SetDesktopPageContext(bool isOnActivePage, bool isEditMode)
    {
        _isOnActivePage = isOnActivePage;
        _isEditMode = isEditMode;

        if (_isOnActivePage && _isAttached && !string.IsNullOrEmpty(_currentPath))
        {
            RefreshCurrentDirectory();
        }
    }

    public void SetComponentPlacementContext(string componentId, string? placementId)
    {
        _componentId = string.IsNullOrWhiteSpace(componentId)
            ? BuiltInComponentIds.DesktopFileManager
            : componentId.Trim();
        _placementId = placementId?.Trim() ?? string.Empty;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        AttachedToVisualTree -= OnAttachedToVisualTree;
        DetachedFromVisualTree -= OnDetachedFromVisualTree;
        SizeChanged -= OnSizeChanged;

        _gestureStates.Clear();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _ = sender;
        _ = e;
        _isAttached = true;

        if (_isOnActivePage)
        {
            RefreshCurrentDirectory();
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _ = sender;
        _ = e;
        _isAttached = false;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        ApplyLayoutMetrics();
    }

    private void ApplyLayoutMetrics()
    {
        var scale = ResolveScale();
        var width = Bounds.Width > 1 ? Bounds.Width : _currentCellSize * 4;

        var buttonSize = Math.Clamp(32 * scale, 28, 40);
        var iconSize = Math.Clamp(14 * scale, 12, 18);
        var pathFontSize = Math.Clamp(13 * scale, 11, 16);

        BackButton.Width = buttonSize;
        BackButton.Height = buttonSize;
        BackButton.CornerRadius = new CornerRadius(buttonSize / 2);

        HomeButton.Width = buttonSize;
        HomeButton.Height = buttonSize;
        HomeButton.CornerRadius = new CornerRadius(buttonSize / 2);

        RefreshButton.Width = buttonSize;
        RefreshButton.Height = buttonSize;
        RefreshButton.CornerRadius = new CornerRadius(buttonSize / 2);

        PathTextBlock.FontSize = pathFontSize;

        if (BackButton.Content is SymbolIcon backIcon)
        {
            backIcon.FontSize = iconSize;
        }

        if (HomeButton.Content is SymbolIcon homeIcon)
        {
            homeIcon.FontSize = iconSize;
        }

        if (RefreshButton.Content is SymbolIcon refreshIcon)
        {
            refreshIcon.FontSize = iconSize;
        }
    }

    private double ResolveScale()
    {
        var cellScale = Math.Clamp(_currentCellSize / 48d, 0.72, 2.2);
        var widthScale = Bounds.Width > 1 ? Math.Clamp(Bounds.Width / 280d, 0.72, 2.4) : 1;
        var heightScale = Bounds.Height > 1 ? Math.Clamp(Bounds.Height / 280d, 0.72, 2.4) : 1;
        return Math.Clamp(Math.Min(cellScale, Math.Min(widthScale, heightScale)), 0.72, 2.2);
    }

    private void OnBackButtonClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (_currentHistoryIndex > 0)
        {
            _currentHistoryIndex--;
            var path = _navigationHistory[_currentHistoryIndex];
            LoadDirectory(path, addToHistory: false);
        }
        else if (_currentHistoryIndex == 0 && _navigationHistory.Count > 0)
        {
            NavigateToDrives();
        }
    }

    private void OnHomeButtonClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        NavigateToDrives();
    }

    private void OnRefreshButtonClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        RefreshCurrentDirectory();
    }

    private void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not FileSystemItem item)
        {
            return;
        }

        var pointer = e.GetCurrentPoint(border);
        var pointerId = e.Pointer.Id;
        var position = pointer.Position;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        _gestureStates[pointerId] = new PointerGestureState(position, timestamp, item, border);

        e.Pointer.Capture(border);
    }

    private void OnItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Border border)
        {
            return;
        }

        var pointerId = e.Pointer.Id;
        if (!_gestureStates.TryGetValue(pointerId, out var state))
        {
            return;
        }

        var currentPoint = e.GetCurrentPoint(border);
        var distance = Math.Sqrt(
            Math.Pow(currentPoint.Position.X - state.StartPosition.X, 2) +
            Math.Pow(currentPoint.Position.Y - state.StartPosition.Y, 2)
        );

        if (distance > TapMovementThreshold)
        {
            _gestureStates.Remove(pointerId);
            e.Pointer.Capture(null);
        }
    }

    private void OnItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Border border)
        {
            return;
        }

        var pointerId = e.Pointer.Id;
        if (!_gestureStates.Remove(pointerId, out var state))
        {
            return;
        }

        e.Pointer.Capture(null);

        var currentPoint = e.GetCurrentPoint(border);
        var distance = Math.Sqrt(
            Math.Pow(currentPoint.Position.X - state.StartPosition.X, 2) +
            Math.Pow(currentPoint.Position.Y - state.StartPosition.Y, 2)
        );

        var elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - state.StartTime;

        if (distance <= TapMovementThreshold && elapsed <= TapTimeThresholdMs)
        {
            if (state.Item.IsDirectory)
            {
                LoadDirectory(state.Item.FullPath, addToHistory: true);
            }
            else
            {
                OpenFile(state.Item.FullPath);
            }
        }
    }

    private void NavigateToDrives()
    {
        _navigationHistory.Clear();
        _currentHistoryIndex = -1;
        _currentPath = string.Empty;

        try
        {
            var drives = new List<FileSystemItem>();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!drive.IsReady)
                        {
                            continue;
                        }

                        var item = FileSystemItem.FromDriveInfo(drive);
                        drives.Add(item);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn("FileManagerWidget", $"Failed to access drive: {drive?.Name}", ex);
                    }
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                drives.Add(new FileSystemItem
                {
                    Name = "根目录",
                    FullPath = "/",
                    ItemType = FileSystemItemType.Directory
                });

                var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(homePath) && Directory.Exists(homePath))
                {
                    drives.Add(new FileSystemItem
                    {
                        Name = "主目录",
                        FullPath = homePath,
                        ItemType = FileSystemItemType.Directory
                    });
                }

                var linuxMountPoints = new[] { "/mnt", "/media", "/run/media" };
                foreach (var mount in linuxMountPoints)
                {
                    if (Directory.Exists(mount))
                    {
                        drives.Add(new FileSystemItem
                        {
                            Name = mount,
                            FullPath = mount,
                            ItemType = FileSystemItemType.Directory
                        });
                    }
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                drives.Add(new FileSystemItem
                {
                    Name = "根目录",
                    FullPath = "/",
                    ItemType = FileSystemItemType.Directory
                });

                drives.Add(new FileSystemItem
                {
                    Name = "用户",
                    FullPath = "/Users",
                    ItemType = FileSystemItemType.Directory
                });

                drives.Add(new FileSystemItem
                {
                    Name = "应用程序",
                    FullPath = "/Applications",
                    ItemType = FileSystemItemType.Directory
                });

                var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(homePath) && Directory.Exists(homePath))
                {
                    drives.Add(new FileSystemItem
                    {
                        Name = "个人",
                        FullPath = homePath,
                        ItemType = FileSystemItemType.Directory
                    });
                }

                if (Directory.Exists("/Volumes"))
                {
                    foreach (var volume in Directory.GetDirectories("/Volumes"))
                    {
                        drives.Add(new FileSystemItem
                        {
                            Name = Path.GetFileName(volume),
                            FullPath = volume,
                            ItemType = FileSystemItemType.Directory
                        });
                    }
                }
            }

            RenderFileItems(drives);
            PathTextBlock.Text = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "此电脑" : "文件系统";

            UpdateEmptyState(drives.Count == 0, "没有可用的位置");
            ErrorStatePanel.IsVisible = false;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("FileManagerWidget", "Failed to load drives.", ex);
            ShowError("无法加载位置列表");
        }
    }

    private void LoadDirectory(string path, bool addToHistory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            NavigateToDrives();
            return;
        }

        try
        {
            var directoryInfo = new DirectoryInfo(path);

            if (!directoryInfo.Exists)
            {
                ShowError("文件夹不存在");
                return;
            }

            var items = new List<FileSystemItem>();

            // 添加子文件夹
            try
            {
                var directories = directoryInfo.GetDirectories()
                    .Where(d => (d.Attributes & FileAttributes.Hidden) == 0)
                    .OrderBy(d => d.Name)
                    .Select(FileSystemItem.FromDirectoryInfo);
                items.AddRange(directories);
            }
            catch (UnauthorizedAccessException)
            {
                // 忽略无权限访问的文件夹
            }

            // 添加文件
            try
            {
                var files = directoryInfo.GetFiles()
                    .Where(f => (f.Attributes & FileAttributes.Hidden) == 0)
                    .OrderBy(f => f.Name)
                    .Select(FileSystemItem.FromFileInfo);
                items.AddRange(files);
            }
            catch (UnauthorizedAccessException)
            {
                // 忽略无权限访问的文件
            }

            RenderFileItems(items);
            _currentPath = path;
            PathTextBlock.Text = FormatPathForDisplay(path);

            if (addToHistory)
            {
                // 移除当前位置之后的历史记录
                if (_currentHistoryIndex < _navigationHistory.Count - 1)
                {
                    _navigationHistory.RemoveRange(_currentHistoryIndex + 1, _navigationHistory.Count - _currentHistoryIndex - 1);
                }

                _navigationHistory.Add(path);
                _currentHistoryIndex = _navigationHistory.Count - 1;
            }

            UpdateEmptyState(items.Count == 0, "文件夹为空");
            ErrorStatePanel.IsVisible = false;
        }
        catch (UnauthorizedAccessException)
        {
            ShowError("没有权限访问此文件夹");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("FileManagerWidget", $"Failed to load directory: {path}", ex);
            ShowError("无法加载文件夹内容");
        }
    }

    private void RenderFileItems(List<FileSystemItem> items)
    {
        FileItemsControl.ItemsSource = null;
        FileItemsControl.Items.Clear();

        foreach (var item in items)
        {
            var itemControl = CreateFileItemControl(item);
            FileItemsControl.Items.Add(itemControl);
        }
    }

    private Control CreateFileItemControl(FileSystemItem item)
    {
        var scale = ResolveScale();
        var itemWidth = Math.Clamp(72 * scale, 64, 96);
        var itemHeight = Math.Clamp(80 * scale, 72, 108);
        var iconSize = Math.Clamp(32 * scale, 24, 40);
        var fontSize = Math.Clamp(11 * scale, 10, 14);

        var textBrush = this.FindResource("AdaptiveTextPrimaryBrush") as IBrush ?? new SolidColorBrush(Colors.White);

        var border = new Border
        {
            Width = itemWidth,
            Height = itemHeight,
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Colors.Transparent),
            Cursor = new Cursor(StandardCursorType.Hand),
            DataContext = item
        };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(4)
        };

        var iconImage = CreateSystemIconImage(item, iconSize);

        var textBlock = new TextBlock
        {
            Text = item.Name,
            FontSize = fontSize,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
            TextWrapping = TextWrapping.Wrap,
            Foreground = textBrush
        };

        if (iconImage is not null)
        {
            grid.Children.Add(iconImage);
            Grid.SetRow(iconImage, 0);
        }

        grid.Children.Add(textBlock);
        Grid.SetRow(textBlock, 1);

        border.Child = grid;

        ToolTip.SetTip(border, item.Name);

        border.PointerPressed += OnItemPointerPressed;
        border.PointerMoved += OnItemPointerMoved;
        border.PointerReleased += OnItemPointerReleased;

        return border;
    }

    private Control? CreateSystemIconImage(FileSystemItem item, double iconSize)
    {
        byte[]? pngBytes = null;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                pngBytes = item.ItemType switch
                {
                    FileSystemItemType.Drive => GetDriveIconBytes(item.FullPath),
                    FileSystemItemType.Directory => WindowsIconService.TryGetSystemFolderIconPngBytes(),
                    _ => WindowsIconService.TryGetIconPngBytes(item.FullPath)
                };
            }
            else if (OperatingSystem.IsLinux())
            {
                pngBytes = item.ItemType switch
                {
                    FileSystemItemType.Drive => LinuxIconService.TryGetDriveIconPngBytes(),
                    FileSystemItemType.Directory => LinuxIconService.TryGetSystemFolderIconPngBytes(),
                    _ => LinuxIconService.TryGetIconPngBytes(item.FullPath)
                };
            }
            else if (OperatingSystem.IsMacOS())
            {
                pngBytes = item.ItemType switch
                {
                    FileSystemItemType.Drive => MacIconService.TryGetDriveIconPngBytes(),
                    FileSystemItemType.Directory => MacIconService.TryGetSystemFolderIconPngBytes(),
                    _ => MacIconService.TryGetIconPngBytes(item.FullPath)
                };
            }
        }
        catch
        {
            pngBytes = null;
        }

        if (pngBytes is not null)
        {
            try
            {
                using var stream = new MemoryStream(pngBytes);
                var bitmap = new Bitmap(stream);
                return new Image
                {
                    Source = bitmap,
                    Width = iconSize,
                    Height = iconSize,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Stretch = Stretch.Uniform
                };
            }
            catch
            {
            }
        }

        return CreateFallbackIconImage(item, iconSize);
    }

    private static byte[]? GetDriveIconBytes(string drivePath)
    {
        if (string.IsNullOrWhiteSpace(drivePath))
        {
            return null;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (Directory.Exists(drivePath))
                {
                    return WindowsIconService.TryGetIconPngBytes(drivePath);
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                return LinuxIconService.TryGetDriveIconPngBytes();
            }
            else if (OperatingSystem.IsMacOS())
            {
                return MacIconService.TryGetDriveIconPngBytes();
            }
        }
        catch
        {
        }

        if (OperatingSystem.IsWindows())
        {
            return WindowsIconService.TryGetSystemFolderIconPngBytes();
        }
        else if (OperatingSystem.IsLinux())
        {
            return LinuxIconService.TryGetSystemFolderIconPngBytes();
        }
        else if (OperatingSystem.IsMacOS())
        {
            return MacIconService.TryGetSystemFolderIconPngBytes();
        }

        return null;
    }

    private Control CreateFallbackIconImage(FileSystemItem item, double iconSize)
    {
        var symbol = item.ItemType switch
        {
            FileSystemItemType.Drive => FluentIcons.Common.Symbol.HardDrive,
            FileSystemItemType.Directory => FluentIcons.Common.Symbol.Folder,
            _ => FluentIcons.Common.Symbol.Document
        };

        var iconBrush = item.ItemType == FileSystemItemType.File
            ? this.FindResource("AdaptiveTextSecondaryBrush") as IBrush ?? new SolidColorBrush(Colors.Gray)
            : this.FindResource("AdaptiveAccentBrush") as IBrush ?? new SolidColorBrush(Colors.DodgerBlue);

        return new SymbolIcon
        {
            Symbol = symbol,
            FontSize = iconSize,
            Foreground = iconBrush,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
    }

    private void RefreshCurrentDirectory()
    {
        if (string.IsNullOrEmpty(_currentPath))
        {
            NavigateToDrives();
        }
        else
        {
            LoadDirectory(_currentPath, addToHistory: false);
        }
    }

    private void UpdateEmptyState(bool isEmpty, string message)
    {
        EmptyStatePanel.IsVisible = isEmpty;
        EmptyStateTextBlock.Text = message;
        FileItemsControl.IsVisible = !isEmpty;
    }

    private void ShowError(string message)
    {
        ErrorStatePanel.IsVisible = true;
        ErrorStateTextBlock.Text = message;
        FileItemsControl.IsVisible = false;
        EmptyStatePanel.IsVisible = false;
    }

    private static void OpenFile(string filePath)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(filePath)
                {
                    UseShellExecute = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", filePath);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", filePath);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("FileManagerWidget", $"Failed to open file: {filePath}", ex);
        }
    }

    private static string FormatPathForDisplay(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "此电脑" : "文件系统";
        }

        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? '\\' : '/';

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (path.Length <= 3 && path.EndsWith(":\\", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var driveInfo = new DriveInfo(path.Substring(0, 1));
                    if (!string.IsNullOrWhiteSpace(driveInfo.VolumeLabel))
                    {
                        return $"{driveInfo.VolumeLabel} ({path.Substring(0, 2)})";
                    }
                }
                catch
                {
                }
                return path;
            }
        }
        else
        {
            if (path == "/")
            {
                return "根目录";
            }

            if (path == Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
            {
                return "主目录";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (path == "/Applications")
                {
                    return "应用程序";
                }

                if (path == "/Users")
                {
                    return "用户";
                }

                if (path.StartsWith("/Volumes/"))
                {
                    return Path.GetFileName(path);
                }
            }
        }

        var parts = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 3)
        {
            return path;
        }

        return $"{parts[0]}{separator}...{separator}{parts[^2]}{separator}{parts[^1]}";
    }
}
