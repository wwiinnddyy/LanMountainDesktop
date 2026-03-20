using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.DesktopComponents.Runtime;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public enum ClockDisplayFormat
{
    HourMinuteSecond,  // HH:mm:ss
    HourMinute          // HH:mm
}

public partial class ClockWidget : UserControl, IDesktopComponentWidget, ITimeZoneAwareComponentWidget
{
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    private TimeZoneService? _timeZoneService;
    private ClockDisplayFormat _displayFormat = ClockDisplayFormat.HourMinuteSecond;
    private bool _transparentBackground;
    private double _lastAppliedCellSize = 100;

    public ClockWidget()
    {
        InitializeComponent();

        _timer.Tick += OnTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        UpdateClock();
    }

    public ClockDisplayFormat DisplayFormat
    {
        get => _displayFormat;
        set
        {
            _displayFormat = value;
            UpdateClock();
        }
    }

    public bool TransparentBackground
    {
        get => _transparentBackground;
        set
        {
            if (_transparentBackground == value)
            {
                return;
            }

            _transparentBackground = value;
            ApplyChrome();
            ApplyCellSize(_lastAppliedCellSize);
        }
    }

    public void SetDisplayFormat(ClockDisplayFormat format)
    {
        DisplayFormat = format;
    }

    public void SetTransparentBackground(bool transparentBackground)
    {
        TransparentBackground = transparentBackground;
    }

    public void SetTimeZoneService(TimeZoneService timeZoneService)
    {
        ClearTimeZoneService();
        _timeZoneService = timeZoneService;
        _timeZoneService.TimeZoneChanged += OnTimeZoneChanged;
        UpdateClock();
    }

    public void ClearTimeZoneService()
    {
        if (_timeZoneService is null)
        {
            return;
        }

        _timeZoneService.TimeZoneChanged -= OnTimeZoneChanged;
        _timeZoneService = null;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        UpdateClock();
        _timer.Start();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        UpdateClock();
    }

    private void OnTimeZoneChanged(object? sender, EventArgs e)
    {
        UpdateClock();
    }

    private void UpdateClock()
    {
        var now = _timeZoneService?.GetCurrentTime() ?? DateTime.Now;
        
        MainTimeTextBlock.Text = now.ToString("HH:mm", CultureInfo.CurrentCulture);
        SecondsTextBlock.Text = now.ToString("ss", CultureInfo.CurrentCulture);
        
        SecondsTextBlock.IsVisible = _displayFormat == ClockDisplayFormat.HourMinuteSecond;
        ApplyTypographyLayout();
    }

    public void ApplyCellSize(double cellSize)
    {
        _lastAppliedCellSize = cellSize;
        var layoutScale = Math.Clamp((cellSize / 44d) * (0.9d + (ComponentChromeCornerRadiusHelper.ResolveScale() * 0.1d)), 0.65d, 1.95d);

        // --- Class Island “满盈”风格算法 ---
        
        // 1. 计算组件高度：保持与任务栏核心比例一致 (0.74x)
        var targetHeight = Math.Clamp(cellSize * 0.74, 34, 74);
        RootBorder.Height = targetHeight;
        
        // 2. 动态圆角：确保始终是完美的胶囊半圆
        RootBorder.CornerRadius = new CornerRadius(targetHeight / 2);
        RootBorder.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        
        // 3. 核心：满盈字阶 (Filled Typography)
        // 使主时间文字占据容器高度的 ~68%，产生饱满的视觉张力
        var mainFontSize = targetHeight * 0.68 * layoutScale;
        MainTimeTextBlock.FontSize = mainFontSize;
        MainTimeTextBlock.FontWeight = FontWeight.SemiBold;
        
        // 4. 次级信息：秒数维持 0.7x 比例，并增强透明度呼吸感
        SecondsTextBlock.FontSize = mainFontSize * 0.7;
        SecondsTextBlock.Opacity = 0.55;
        
        // 5. 视觉占比：占据约 2.2 个单元格的感官宽度 (cellSize * 2 + gaps)
        RootBorder.MinWidth = cellSize * 2.2;

        // 6. 间距微调
        if (MainTimeTextBlock.Parent is StackPanel panel)
        {
            panel.Spacing = Math.Clamp(cellSize * 0.06 * layoutScale, 2, 8);
        }

        if (_transparentBackground)
        {
            RootBorder.MinWidth = 0;
            RootBorder.Padding = new Thickness(Math.Clamp(cellSize * 0.06 * layoutScale, 4, 10), 0);
            return;
        }

        // 确保清除可能存在的固定 Padding，由代码控制“紧密感”
        RootBorder.MinWidth = cellSize * 2.2;
        RootBorder.Padding = new Thickness(Math.Clamp(cellSize * 0.15 * layoutScale, 12, 24), 0);
        ApplyTypographyLayout();
    }

    private void ApplyTypographyLayout()
    {
        var layoutScale = Math.Clamp((_lastAppliedCellSize / 44d) * (0.9d + (ComponentChromeCornerRadiusHelper.ResolveScale() * 0.1d)), 0.65d, 1.95d);
        var availableWidth = Math.Max(1, RootBorder.Bounds.Width > 1 ? RootBorder.Bounds.Width : Math.Max(1, _lastAppliedCellSize * 2.2));
        var availableHeight = Math.Max(1, RootBorder.Bounds.Height > 1 ? RootBorder.Bounds.Height : Math.Clamp(_lastAppliedCellSize * 0.74, 34, 74));
        var contentWidth = Math.Max(1, availableWidth - RootBorder.Padding.Left - RootBorder.Padding.Right);
        var contentHeight = Math.Max(1, availableHeight - RootBorder.Padding.Top - RootBorder.Padding.Bottom);

        var mainLayout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
            MainTimeTextBlock.Text,
            contentWidth * (SecondsTextBlock.IsVisible ? 0.78d : 0.84d),
            contentHeight * 0.80d,
            minLines: 1,
            maxLines: 1,
            minFontSize: Math.Clamp(18 * layoutScale, 16, 28),
            maxFontSize: Math.Clamp(44 * layoutScale, 28, 64),
            weightCandidates: new[] { FontWeight.SemiBold, FontWeight.Medium },
            lineHeightFactor: 0.96d);
        MainTimeTextBlock.FontSize = mainLayout.FontSize;
        MainTimeTextBlock.FontWeight = mainLayout.Weight;

        if (SecondsTextBlock.IsVisible)
        {
            var secondsLayout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
                SecondsTextBlock.Text,
                contentWidth * 0.28d,
                contentHeight * 0.46d,
                minLines: 1,
                maxLines: 1,
                minFontSize: Math.Clamp(11 * layoutScale, 9, 18),
                maxFontSize: Math.Clamp(28 * layoutScale, 14, 34),
                weightCandidates: new[] { FontWeight.Medium, FontWeight.Normal },
                lineHeightFactor: 0.96d);
            SecondsTextBlock.FontSize = secondsLayout.FontSize;
            SecondsTextBlock.FontWeight = secondsLayout.Weight;
            SecondsTextBlock.Opacity = 0.55;
        }

        if (MainTimeTextBlock.Parent is StackPanel panel)
        {
            panel.Spacing = Math.Clamp(contentHeight * 0.06 * layoutScale, 2, 8);
        }

        if (_transparentBackground)
        {
            RootBorder.MinWidth = 0;
            RootBorder.Padding = new Thickness(Math.Clamp(_lastAppliedCellSize * 0.06 * layoutScale, 4, 10), 0);
            return;
        }

        RootBorder.MinWidth = _lastAppliedCellSize * 2.2;
        RootBorder.Padding = new Thickness(Math.Clamp(_lastAppliedCellSize * 0.15 * layoutScale, 12, 24), 0);
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
}
