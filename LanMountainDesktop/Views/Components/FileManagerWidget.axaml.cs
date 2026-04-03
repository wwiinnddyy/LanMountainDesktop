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
        _ = e;

        if (sender is not Border border || border.DataContext is not FileSystemItem item)
        {
            return;
        }

        if (item.IsDirectory)
        {
            LoadDirectory(item.FullPath, addToHistory: true);
        }
        else
        {
            OpenFile(item.FullPath);
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

            RenderFileItems(drives);
            PathTextBlock.Text = "此电脑";

            UpdateEmptyState(drives.Count == 0, "没有可用的驱动器");
            ErrorStatePanel.IsVisible = false;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("FileManagerWidget", "Failed to load drives.", ex);
            ShowError("无法加载驱动器列表");
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

        // 根据类型选择图标
        var symbol = item.ItemType switch
        {
            FileSystemItemType.Drive => FluentIcons.Common.Symbol.HardDrive,
            FileSystemItemType.Directory => FluentIcons.Common.Symbol.Folder,
            _ => FluentIcons.Common.Symbol.Document
        };

        var iconBrush = item.ItemType == FileSystemItemType.File
            ? this.FindResource("AdaptiveTextSecondaryBrush") as IBrush ?? new SolidColorBrush(Colors.Gray)
            : this.FindResource("AdaptiveAccentBrush") as IBrush ?? new SolidColorBrush(Colors.DodgerBlue);

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

        // 图标
        var icon = new SymbolIcon
        {
            Symbol = symbol,
            FontSize = iconSize,
            Foreground = iconBrush,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        // 名称
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

        grid.Children.Add(icon);
        Grid.SetRow(icon, 0);

        grid.Children.Add(textBlock);
        Grid.SetRow(textBlock, 1);

        border.Child = grid;

        // 添加提示
        ToolTip.SetTip(border, item.Name);

        // 添加点击事件
        border.PointerPressed += OnItemPointerPressed;

        return border;
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
            return "此电脑";
        }

        // 如果是驱动器根目录，显示驱动器名称
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
                // 忽略错误，返回默认格式
            }
            return path;
        }

        // 智能路径截断：保留根目录和最后两级
        var parts = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 3)
        {
            return path;
        }

        // 格式：根目录\...\父文件夹\当前文件夹
        return $"{parts[0]}\\...\\{parts[^2]}\\{parts[^1]}";
    }
}
