using System;
using System.Linq;
using System.Net.NetworkInformation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using FluentIcons.Avalonia;
using FluentIcons.Common;
using LanMountainDesktop.Services;
using Symbol = FluentIcons.Common.Symbol;

namespace LanMountainDesktop.Views.Components;

public partial class NetworkSpeedWidget : UserControl, IDesktopComponentWidget
{
    private readonly DispatcherTimer _timer = new();
    private readonly DispatcherTimer _networkTypeTimer = new();
    private NetworkInterface? _selectedInterface;
    private long _lastBytesReceived;
    private long _lastBytesSent;
    private bool _isFirstUpdate = true;
    private double _lastAppliedCellSize = 100;
    private bool _transparentBackground;
    private string _displayMode = "Both"; // "Upload", "Download", "Both"
    private bool _showNetworkTypeIcon;
    private string _fontSize = "Medium"; // Small, Medium, Large

    public NetworkSpeedWidget()
    {
        InitializeComponent();
        SetupTimer();
        SelectBestInterface();
        UpdateDisplayMode();
        UpdateNetworkTypeIcon();
    }

    public string DisplayMode
    {
        get => _displayMode;
        set
        {
            if (_displayMode == value) return;
            _displayMode = value;
            UpdateDisplayMode();
        }
    }

    public bool TransparentBackground
    {
        get => _transparentBackground;
        set
        {
            if (_transparentBackground == value) return;
            _transparentBackground = value;
            ApplyChrome();
            ApplyCellSize(_lastAppliedCellSize);
        }
    }

    public bool ShowNetworkTypeIcon
    {
        get => _showNetworkTypeIcon;
        set
        {
            if (_showNetworkTypeIcon == value) return;
            _showNetworkTypeIcon = value;
            UpdateNetworkTypeIcon();
        }
    }

    public void SetDisplayMode(string mode)
    {
        DisplayMode = mode;
    }

    public void SetTransparentBackground(bool transparent)
    {
        TransparentBackground = transparent;
    }

    public void SetShowNetworkTypeIcon(bool show)
    {
        ShowNetworkTypeIcon = show;
    }

    public string WidgetFontSize
    {
        get => _fontSize;
        set
        {
            _fontSize = value;
            ApplyCellSize(_lastAppliedCellSize);
        }
    }

    public void SetFontSize(string fontSize)
    {
        WidgetFontSize = fontSize;
    }

    private void SetupTimer()
    {
        // 网速更新定时器（每秒）
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => UpdateSpeed();
        _timer.Start();

        // 网络类型检测定时器（每500ms，满足响应延迟要求）
        _networkTypeTimer.Interval = TimeSpan.FromMilliseconds(500);
        _networkTypeTimer.Tick += (_, _) => UpdateNetworkTypeIcon();
        _networkTypeTimer.Start();
    }

    private void SelectBestInterface()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                .Where(ni => ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Where(ni => ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .Where(ni => !ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                .Where(ni => !ni.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // 优先选择有流量的物理网卡
            _selectedInterface = interfaces
                .OrderByDescending(ni => ni.GetIPv4Statistics().BytesReceived + ni.GetIPv4Statistics().BytesSent)
                .FirstOrDefault();

            // 如果没有找到，选择第一个活动的非虚拟网卡
            _selectedInterface ??= interfaces.FirstOrDefault();

            if (_selectedInterface != null)
            {
                var stats = _selectedInterface.GetIPv4Statistics();
                _lastBytesReceived = stats.BytesReceived;
                _lastBytesSent = stats.BytesSent;
            }
        }
        catch
        {
            // 忽略错误，下次重试
        }
    }

    private void UpdateSpeed()
    {
        try
        {
            // 如果当前网卡不可用，尝试重新选择
            if (_selectedInterface == null ||
                _selectedInterface.OperationalStatus != OperationalStatus.Up)
            {
                SelectBestInterface();
            }

            if (_selectedInterface == null)
            {
                UploadSpeedTextBlock.Text = "--";
                DownloadSpeedTextBlock.Text = "--";
                return;
            }

            var stats = _selectedInterface.GetIPv4Statistics();
            var currentBytesReceived = stats.BytesReceived;
            var currentBytesSent = stats.BytesSent;

            if (_isFirstUpdate)
            {
                _lastBytesReceived = currentBytesReceived;
                _lastBytesSent = currentBytesSent;
                _isFirstUpdate = false;
                return;
            }

            // 计算速度（每秒字节数）
            var downloadBytes = currentBytesReceived - _lastBytesReceived;
            var uploadBytes = currentBytesSent - _lastBytesSent;

            // 处理计数器重置的情况
            if (downloadBytes < 0) downloadBytes = 0;
            if (uploadBytes < 0) uploadBytes = 0;

            UploadSpeedTextBlock.Text = FormatSpeed(uploadBytes);
            DownloadSpeedTextBlock.Text = FormatSpeed(downloadBytes);

            _lastBytesReceived = currentBytesReceived;
            _lastBytesSent = currentBytesSent;
        }
        catch
        {
            // 错误时显示 --
            UploadSpeedTextBlock.Text = "--";
            DownloadSpeedTextBlock.Text = "--";
        }
    }

    private void UpdateNetworkTypeIcon()
    {
        try
        {
            if (!_showNetworkTypeIcon || NetworkTypeIcon == null)
            {
                if (NetworkTypeIcon != null)
                    NetworkTypeIcon.IsVisible = false;
                return;
            }

            // 获取当前活动的网络接口
            var activeInterface = GetActiveNetworkInterface();

            if (activeInterface == null)
            {
                // 无网络连接
                NetworkTypeIcon.Symbol = Symbol.DismissCircle;
                NetworkTypeIcon.IsVisible = true;
                return;
            }

            // 根据网络类型设置图标
            switch (activeInterface.NetworkInterfaceType)
            {
                case NetworkInterfaceType.Wireless80211:
                    // WiFi
                    NetworkTypeIcon.Symbol = Symbol.WiFi;
                    break;

                case NetworkInterfaceType.Ethernet:
                    // 有线网络 - 检查是否是移动网络热点
                    if (IsLikelyMobileHotspot(activeInterface))
                    {
                        NetworkTypeIcon.Symbol = Symbol.Phone;
                    }
                    else
                    {
                        NetworkTypeIcon.Symbol = Symbol.PlugConnected;
                    }
                    break;

                default:
                    // 其他类型，尝试根据描述判断
                    var symbol = GetSymbolFromDescription(activeInterface.Description);
                    NetworkTypeIcon.Symbol = symbol;
                    break;
            }

            NetworkTypeIcon.IsVisible = true;
        }
        catch
        {
            // 错误时隐藏图标
            if (NetworkTypeIcon != null)
                NetworkTypeIcon.IsVisible = false;
        }
    }

    private NetworkInterface? GetActiveNetworkInterface()
    {
        try
        {
            // 优先使用当前选中的网卡
            if (_selectedInterface != null &&
                _selectedInterface.OperationalStatus == OperationalStatus.Up)
            {
                return _selectedInterface;
            }

            // 否则查找最佳网卡
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                .Where(ni => ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Where(ni => ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .ToList();

            // 优先返回有流量的网卡
            return interfaces
                .OrderByDescending(ni => ni.GetIPv4Statistics().BytesReceived + ni.GetIPv4Statistics().BytesSent)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsLikelyMobileHotspot(NetworkInterface ni)
    {
        // 通过描述判断是否是移动热点
        var desc = ni.Description.ToLowerInvariant();
        return desc.Contains("mobile") ||
               desc.Contains("cellular") ||
               desc.Contains("phone") ||
               desc.Contains("tether");
    }

    private static Symbol GetSymbolFromDescription(string description)
    {
        var desc = description.ToLowerInvariant();

        if (desc.Contains("wifi") || desc.Contains("wi-fi") || desc.Contains("wireless"))
            return Symbol.WiFi;

        if (desc.Contains("ethernet") || desc.Contains("lan") || desc.Contains("wired"))
            return Symbol.PlugConnected;

        if (desc.Contains("cellular") || desc.Contains("mobile") || desc.Contains("lte") || desc.Contains("5g") || desc.Contains("4g"))
            return Symbol.Phone;

        if (desc.Contains("bluetooth"))
            return Symbol.Bluetooth;

        // 默认使用 Globe 图标
        return Symbol.Globe;
    }

    private static string FormatSpeed(long bytesPerSecond)
    {
        // 根据数值大小选择合适的单位，确保显示始终在1-99.9范围内
        // 当数值达到100时自动切换到更大的单位
        return bytesPerSecond switch
        {
            >= 100L * 1024 * 1024 * 1024 => FormatWithThreeDigits(bytesPerSecond / (1024.0 * 1024.0 * 1024.0), "G"),
            >= 100L * 1024 * 1024 => FormatWithThreeDigits(bytesPerSecond / (1024.0 * 1024.0), "M"),
            >= 100L * 1024 => FormatWithThreeDigits(bytesPerSecond / 1024.0, "K"),
            >= 100 => FormatWithThreeDigits(bytesPerSecond / 1024.0, "K"),  // 100B+ 显示为 0.1K
            _ => FormatWithThreeDigits(bytesPerSecond, "B")
        };
    }

    /// <summary>
    /// 格式化数字，始终保持3位数字+小数点，确保宽度恒定
    /// 数值范围始终在1-99.9之间
    /// </summary>
    private static string FormatWithThreeDigits(double value, string unit)
    {
        // 始终保持3位数字，小数点始终存在
        // 数值范围: 0.0 - 99.9
        // < 10: 显示两位小数 (如 1.23)
        // >= 10: 显示一位小数 (如 12.3, 99.9)
        string formatted = value switch
        {
            < 10 => $"{value:F2}",   // 1.23
            _ => $"{value:F1}"       // 12.3, 99.9
        };

        return formatted + unit;
    }

    private void UpdateDisplayMode()
    {
        switch (_displayMode)
        {
            case "Upload":
                UploadPanel.IsVisible = true;
                DownloadPanel.IsVisible = false;
                Separator.IsVisible = false;
                break;
            case "Download":
                UploadPanel.IsVisible = false;
                DownloadPanel.IsVisible = true;
                Separator.IsVisible = false;
                break;
            case "Both":
            default:
                UploadPanel.IsVisible = true;
                DownloadPanel.IsVisible = true;
                Separator.IsVisible = true;
                break;
        }
    }

    public void ApplyCellSize(double cellSize)
    {
        _lastAppliedCellSize = cellSize;

        // 计算组件高度：保持与任务栏核心比例一致 (0.74x)
        var targetHeight = Math.Clamp(cellSize * 0.74, 34, 74);
        RootBorder.Height = targetHeight;

        // 主矩形统一到主题主档圆角
        RootBorder.CornerRadius = ResolveUnifiedMainRectangle();
        RootBorder.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;

        // 根据单元格大小和字体大小设置调整字体大小
        var fontSizeMultiplier = _fontSize switch
        {
            "Small" => 0.32,
            "Large" => 0.48,
            _ => 0.4 // Medium (default)
        };
        var fontSize = Math.Clamp(targetHeight * fontSizeMultiplier, 11, 22);
        UploadSpeedTextBlock.FontSize = fontSize;
        DownloadSpeedTextBlock.FontSize = fontSize;

        // 调整图标大小
        if (NetworkTypeIcon != null)
        {
            NetworkTypeIcon.FontSize = Math.Clamp(targetHeight * 0.35, 10, 18);
        }

        // 设置最小和最大宽度
        RootBorder.MinWidth = cellSize * 1.5;
        RootBorder.MaxWidth = cellSize * 5;

        if (_transparentBackground)
        {
            RootBorder.MinWidth = 0;
            RootBorder.Padding = new Thickness(Math.Clamp(cellSize * 0.06, 4, 10), 0);
            return;
        }

        // 确保清除可能存在的固定 Padding，由代码控制"紧密感"
        RootBorder.Padding = new Thickness(Math.Clamp(cellSize * 0.15, 12, 24), 0);
    }

    private void ApplyChrome()
    {
        if (_transparentBackground)
        {
            RootBorder.Classes.Remove("glass-panel");
            RootBorder.Background = Brushes.Transparent;
            RootBorder.BorderBrush = Brushes.Transparent;
            RootBorder.BorderThickness = new Thickness(0);
            RootBorder.BoxShadow = default;
            return;
        }

        if (!RootBorder.Classes.Contains("glass-panel"))
        {
            RootBorder.Classes.Add("glass-panel");
        }

        RootBorder.ClearValue(Border.BackgroundProperty);
        RootBorder.ClearValue(Border.BorderBrushProperty);
        RootBorder.ClearValue(Border.BorderThicknessProperty);
        RootBorder.ClearValue(Border.BoxShadowProperty);
    }

    private CornerRadius ResolveUnifiedMainRectangle() => new(ResolveUnifiedMainRadiusValue());

    private static double ResolveUnifiedMainRadiusValue() =>
        HostAppearanceThemeProvider.GetOrCreate().GetCurrent().CornerRadiusTokens.Lg.TopLeft;

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
        _networkTypeTimer?.Stop();
    }
}
