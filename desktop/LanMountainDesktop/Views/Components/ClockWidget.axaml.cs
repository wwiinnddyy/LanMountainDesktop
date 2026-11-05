using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public enum ClockDisplayFormat
{
    HourMinuteSecond,  // HH:mm:ss
    HourMinute          // HH:mm
}

public partial class ClockWidget : UserControl, IDesktopComponentWidget, ITimeZoneAwareComponentWidget, IDesktopPageVisibilityAwareComponentWidget
{
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    private TimeZoneService? _timeZoneService;
    private ClockDisplayFormat _displayFormat = ClockDisplayFormat.HourMinuteSecond;
    private bool _transparentBackground;
    private double _lastAppliedCellSize = 100;
    private string _fontSize = "Medium"; // Small, Medium, Large
    // 多页桌面里非当前页的组件仍挂在视觉树上（只是被平移出屏），DetachedFromVisualTree 不会来；
    // 不按页停表的话，4 页桌面 × 每页一只钟 = 4 倍的逐秒空转。默认 true：推送不到的宿主（组件库预览等）维持现状。
    private bool _isOnActivePage = true;
    private bool _isAttached;

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
            AlignTimerToDisplayFormat();
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

    public void SetTimeZoneService(TimeZoneService timeZoneService)
    {
        TimeZoneServiceBinding.Attach(
            ref _timeZoneService,
            timeZoneService,
            OnTimeZoneChanged,
            UpdateClock);
    }

    public void ClearTimeZoneService()
    {
        _timeZoneService = TimeZoneServiceBinding.Clear(_timeZoneService, OnTimeZoneChanged);
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        UpdateClock();
        AlignTimerToDisplayFormat();
        if (_isOnActivePage)
        {
            _timer.Start();
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ComponentPagePauseLifetime.Detach(ref _isAttached, _timer);
    }

    public void SetDesktopPageContext(bool isOnActivePage, bool isEditMode)
    {
        _ = isEditMode;
        ComponentPagePauseLifetime.Apply(ref _isOnActivePage, isOnActivePage, _isAttached, [_timer],
            onActive: () =>
            {
                UpdateClock();
                AlignTimerToDisplayFormat();
            });
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        // HH:mm 档里秒位根本不显示，逐秒醒来只是白付 60 倍的唤醒和字符串分配——
        // 把下一次 tick 排到下一个分钟边界上，秒档维持 1s 不变。
        AlignTimerToDisplayFormat();
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
    }

    private void AlignTimerToDisplayFormat()
    {
        if (_displayFormat == ClockDisplayFormat.HourMinute)
        {
            var now = DateTime.Now;
            _timer.Interval = TimeSpan.FromSeconds(now.Second == 0 ? 60 : 60 - now.Second);
        }
        else
        {
            _timer.Interval = TimeSpan.FromSeconds(1);
        }
    }

    public void ApplyCellSize(double cellSize)
    {
        _lastAppliedCellSize = cellSize;

        // --- Class Island “满盈”风格算法 ---
        
        // 1. 计算组件高度：保持与任务栏核心比例一致 (0.74x)
        var targetHeight = Math.Clamp(cellSize * 0.74, 34, 74);
        RootBorder.Height = targetHeight;
        
        // 2. 主矩形统一到主题主档圆角
        RootBorder.CornerRadius = ComponentChromeCornerRadiusHelper.ResolveLgRectangle();
        RootBorder.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        
        // 3. 核心：满盈字阶 (Filled Typography)
        // 使主时间文字占据容器高度的 ~68%，产生饱满的视觉张力
        // 根据字体大小设置调整基础大小
        var fontSizeMultiplier = _fontSize switch
        {
            "Small" => 0.55,
            "Large" => 0.85,
            _ => 0.68 // Medium (default)
        };
        var mainFontSize = targetHeight * fontSizeMultiplier;
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
            panel.Spacing = Math.Clamp(cellSize * 0.06, 2, 8);
        }

        if (_transparentBackground)
        {
            RootBorder.MinWidth = 0;
            RootBorder.Padding = new Thickness(Math.Clamp(cellSize * 0.06, 4, 10), 0);
            return;
        }

        // 确保清除可能存在的固定 Padding，由代码控制“紧密感”
        RootBorder.MinWidth = cellSize * 2.2;
        RootBorder.Padding = new Thickness(Math.Clamp(cellSize * 0.15, 12, 24), 0);
    }

    private void ApplyChrome() => ComponentChromePanel.Apply(RootBorder, _transparentBackground);

}
