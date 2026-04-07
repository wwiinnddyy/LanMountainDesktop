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

public partial class ShortcutWidget : UserControl, IDesktopComponentWidget, IComponentPlacementContextAware, IDisposable
{
    private string _componentId = BuiltInComponentIds.DesktopShortcut;
    private string _placementId = string.Empty;
    private string? _targetPath;
    private string _clickMode = "Double";
    private bool _transparentBackground;
    private double _currentCellSize = 48;
    private bool _isDisposed;

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
        UpdateDisplay();
    }

    public void SetComponentPlacementContext(string componentId, string? placementId)
    {
        _componentId = string.IsNullOrWhiteSpace(componentId)
            ? BuiltInComponentIds.DesktopShortcut
            : componentId.Trim();
        _placementId = placementId?.Trim() ?? string.Empty;
    }

    public void ApplySettings(ComponentSettingsSnapshot snapshot)
    {
        _targetPath = snapshot.ShortcutTargetPath;
        _clickMode = string.Equals(snapshot.ShortcutClickMode, "Single", StringComparison.OrdinalIgnoreCase)
            ? "Single"
            : "Double";
        _transparentBackground = snapshot.ShortcutTransparentBackground;
        UpdateDisplay();
        ApplyChrome();
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = cellSize;
        var iconSize = Math.Clamp(cellSize * 0.5, 24, 64);
        IconImage.Width = iconSize;
        IconImage.Height = iconSize;

        var fontSize = Math.Clamp(cellSize * 0.18, 10, 16);
        NameTextBlock.FontSize = fontSize;
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
            NameTextBlock.Foreground = this.FindResource("AdaptiveTextPrimaryBrush") as IBrush ?? new SolidColorBrush(Colors.White);

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
        NameTextBlock.Foreground = this.FindResource("AdaptiveTextSecondaryBrush") as IBrush ?? new SolidColorBrush(Colors.Gray);

        var iconBrush = this.FindResource("AdaptiveTextSecondaryBrush") as IBrush ?? new SolidColorBrush(Colors.Gray);
        IconImage.Source = null;
        
        var iconHostContent = new SymbolIcon
        {
            Symbol = FluentIcons.Common.Symbol.Add,
            FontSize = 32,
            Foreground = iconBrush,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        IconHost.Child = iconHostContent;
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
                IconHost.Child = IconImage;
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

        var iconBrush = this.FindResource("AdaptiveAccentBrush") as IBrush ?? new SolidColorBrush(Colors.DodgerBlue);

        IconImage.Source = null;
        var iconHostContent = new SymbolIcon
        {
            Symbol = symbol,
            FontSize = 32,
            Foreground = iconBrush,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        IconHost.Child = iconHostContent;
    }

    private void ApplyChrome()
    {
        if (_transparentBackground)
        {
            RootBorder.Classes.Remove("glass-panel");
            RootBorder.Background = Brushes.Transparent;
            RootBorder.BorderBrush = Brushes.Transparent;
            RootBorder.BorderThickness = new Thickness(0);
            return;
        }

        if (!RootBorder.Classes.Contains("glass-panel"))
        {
            RootBorder.Classes.Add("glass-panel");
        }

        RootBorder.ClearValue(Border.BackgroundProperty);
        RootBorder.ClearValue(Border.BorderBrushProperty);
        RootBorder.ClearValue(Border.BorderThicknessProperty);
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
        _gestureStates.Clear();
    }
}
