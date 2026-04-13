using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FluentIcons.Avalonia;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class ShortcutWidget : UserControl, IDesktopComponentWidget, IComponentPlacementContextAware, IComponentSettingsContextAware, IDisposable
{
    private string _componentId = BuiltInComponentIds.DesktopShortcut;
    private string _placementId = string.Empty;
    private string? _targetPath;
    private string _clickMode = "Double";
    private bool _showBackground = true;
    private double _currentCellSize = 48;
    private bool _isDisposed;
    private bool _chromeApplied;

    private const double TapMovementThreshold = 10;
    private const long TapTimeThresholdMs = 500;

    private readonly Dictionary<int, PointerGestureState> _gestureStates = new();

    private record PointerGestureState(
        Point StartPosition,
        long StartTime
    );

    public ShortcutWidget()
    {
        InitializeComponent();
        DoubleTapped += OnDoubleTapped;
        Loaded += OnLoaded;
        UpdateDisplay();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // ApplyChrome() may have been called before the control was attached to the visual tree,
        // causing FindResource() to fail. Re-apply now that resources are available.
        if (!_chromeApplied)
        {
            ApplyChrome();
        }

        // Subscribe to theme changes so the background follows theme updates.
        var themeService = HostAppearanceThemeProvider.GetOrCreate();
        themeService.Changed += OnAppearanceThemeChanged;
    }

    private void OnAppearanceThemeChanged(object? sender, AppearanceThemeSnapshot e)
    {
        if (_isDisposed || _showBackground)
        {
            ApplyChrome();
        }
    }

    public void SetComponentPlacementContext(string componentId, string? placementId)
    {
        _componentId = string.IsNullOrWhiteSpace(componentId)
            ? BuiltInComponentIds.DesktopShortcut
            : componentId.Trim();
        _placementId = placementId?.Trim() ?? string.Empty;
    }

    public void SetComponentSettingsContext(DesktopComponentSettingsContext context)
    {
        var snapshot = context.ComponentSettingsAccessor.LoadSnapshot<ComponentSettingsSnapshot>();
        ApplySettings(snapshot);
    }

    public void ApplySettings(ComponentSettingsSnapshot snapshot)
    {
        _targetPath = snapshot.ShortcutTargetPath;
        _clickMode = string.Equals(snapshot.ShortcutClickMode, "Single", StringComparison.OrdinalIgnoreCase)
            ? "Single"
            : "Double";
        _showBackground = snapshot.ShortcutShowBackground;
        UpdateDisplay();
        ApplyChrome();
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = cellSize;
        
        // 图标大小：从 cellSize 的 50% 计算，最小 24px，最大 128px
        var iconSize = Math.Clamp(cellSize * 0.5, 24, 128);
        IconImage.Width = iconSize;
        IconImage.Height = iconSize;

        // 字体大小：从 cellSize 的 18% 计算，最小 10px，最大 24px
        var fontSize = Math.Clamp(cellSize * 0.18, 10, 24);
        NameTextBlock.FontSize = fontSize;
        
        // 更新符号图标的大小（如果当前显示的是符号图标）
        if (SymbolIconHost.Content is SymbolIcon symbolIcon)
        {
            symbolIcon.FontSize = iconSize;
        }
    }

    private void UpdateDisplay()
    {
        if (string.IsNullOrWhiteSpace(_targetPath))
        {
            ShowEmptyState();
            return;
        }

        try
        {
            var name = GetDisplayName(_targetPath);
            NameTextBlock.Text = name;
            // 文字颜色由 XAML 中的 DynamicResource 自动适配主题

            LoadIcon(_targetPath);
        }
        catch
        {
            ShowEmptyState();
        }
    }

    private void ShowEmptyState()
    {
        NameTextBlock.Text = "添加快捷方式";
        // 使用次要文字颜色（由主题自动适配）
        NameTextBlock.Foreground = this.FindResource("AdaptiveTextSecondaryBrush") as IBrush;

        var iconBrush = this.FindResource("AdaptiveTextSecondaryBrush") as IBrush;
        
        // 隐藏图片图标，显示符号图标
        IconImage.IsVisible = false;
        IconImage.Source = null;
        
        // 计算图标大小
        var iconSize = Math.Clamp(_currentCellSize * 0.5, 24, 128);
        
        var iconHostContent = new SymbolIcon
        {
            Symbol = FluentIcons.Common.Symbol.Add,
            FontSize = iconSize,
            Foreground = iconBrush,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        SymbolIconHost.Content = iconHostContent;
        SymbolIconHost.IsVisible = true;
    }

    private static string GetDisplayName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "快捷方式";
        }

        try
        {
            if (Directory.Exists(path))
            {
                return Path.GetFileName(path.TrimEnd('\\', '/'));
            }

            var fileName = Path.GetFileNameWithoutExtension(path);
            return string.IsNullOrWhiteSpace(fileName) ? path : fileName;
        }
        catch
        {
            return path;
        }
    }

    private void LoadIcon(string path)
    {
        byte[]? pngBytes = null;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (Directory.Exists(path))
                {
                    pngBytes = WindowsIconService.TryGetSystemFolderIconPngBytes();
                }
                else if (File.Exists(path))
                {
                    pngBytes = WindowsIconService.TryGetIconPngBytes(path);
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                if (Directory.Exists(path))
                {
                    pngBytes = LinuxIconService.TryGetSystemFolderIconPngBytes();
                }
                else if (File.Exists(path))
                {
                    pngBytes = LinuxIconService.TryGetIconPngBytes(path);
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                if (Directory.Exists(path))
                {
                    pngBytes = MacIconService.TryGetSystemFolderIconPngBytes();
                }
                else if (File.Exists(path))
                {
                    pngBytes = MacIconService.TryGetIconPngBytes(path);
                }
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
                IconImage.Source = new Bitmap(stream);
                IconImage.IsVisible = true;
                SymbolIconHost.IsVisible = false;
                return;
            }
            catch
            {
            }
        }

        LoadFallbackIcon(path);
    }

    private void LoadFallbackIcon(string path)
    {
        var symbol = Directory.Exists(path)
            ? FluentIcons.Common.Symbol.Folder
            : FluentIcons.Common.Symbol.Document;

        // 使用强调色（由主题自动适配）
        var iconBrush = this.FindResource("AdaptiveAccentBrush") as IBrush;

        // 隐藏图片图标，显示符号图标
        IconImage.IsVisible = false;
        IconImage.Source = null;
        
        // 计算图标大小
        var iconSize = Math.Clamp(_currentCellSize * 0.5, 24, 128);
        
        var iconHostContent = new SymbolIcon
        {
            Symbol = symbol,
            FontSize = iconSize,
            Foreground = iconBrush,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        SymbolIconHost.Content = iconHostContent;
        SymbolIconHost.IsVisible = true;
    }

    private void ApplyChrome()
    {
        if (!_showBackground)
        {
            RootBorder.Background = Brushes.Transparent;
            RootBorder.BorderBrush = Brushes.Transparent;
            RootBorder.BorderThickness = new Thickness(0);
            _chromeApplied = true;
            return;
        }

        // FindResource requires the control to be attached to the visual tree.
        // If it returns null, _chromeApplied stays false so OnLoaded will retry.
        var background = this.FindResource("AdaptiveSurfaceRaisedBrush") as IBrush;
        var borderBrush = this.FindResource("AdaptiveButtonBorderBrush") as IBrush;

        if (background is null || borderBrush is null)
        {
            _chromeApplied = false;
            return;
        }

        RootBorder.Background = background;
        RootBorder.BorderBrush = borderBrush;
        RootBorder.BorderThickness = new Thickness(1);
        _chromeApplied = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (string.IsNullOrWhiteSpace(_targetPath))
        {
            return;
        }

        var pointer = e.GetCurrentPoint(this);
        var pointerId = e.Pointer.Id;
        var position = pointer.Position;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        _gestureStates[pointerId] = new PointerGestureState(position, timestamp);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var pointerId = e.Pointer.Id;
        if (!_gestureStates.TryGetValue(pointerId, out var state))
        {
            return;
        }

        var currentPoint = e.GetCurrentPoint(this);
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

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        var pointerId = e.Pointer.Id;
        if (!_gestureStates.Remove(pointerId, out var state))
        {
            return;
        }

        e.Pointer.Capture(null);

        var currentPoint = e.GetCurrentPoint(this);
        var distance = Math.Sqrt(
            Math.Pow(currentPoint.Position.X - state.StartPosition.X, 2) +
            Math.Pow(currentPoint.Position.Y - state.StartPosition.Y, 2)
        );

        var elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - state.StartTime;

        if (distance > TapMovementThreshold || elapsed > TapTimeThresholdMs)
        {
            return;
        }

        if (_clickMode == "Single")
        {
            OpenTarget();
        }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_targetPath))
        {
            return;
        }

        if (_clickMode == "Double")
        {
            OpenTarget();
        }
    }

    private void OpenTarget()
    {
        if (string.IsNullOrWhiteSpace(_targetPath))
        {
            return;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(_targetPath)
                {
                    UseShellExecute = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", _targetPath);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", _targetPath);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("ShortcutWidget", $"Failed to open target: {_targetPath}", ex);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        var themeService = HostAppearanceThemeProvider.GetOrCreate();
        themeService.Changed -= OnAppearanceThemeChanged;

        _gestureStates.Clear();
    }
}
