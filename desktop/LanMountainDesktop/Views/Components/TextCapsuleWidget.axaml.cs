using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.Services;
using Markdown.Avalonia;

namespace LanMountainDesktop.Views.Components;

public partial class TextCapsuleWidget : UserControl, IDesktopComponentWidget
{
    private string _text = string.Empty;
    private bool _transparentBackground;
    private double _lastAppliedCellSize = 100;
    private CancellationTokenSource? _debounceCts;

    public TextCapsuleWidget()
    {
        InitializeComponent();
        UpdateDisplay();
    }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value)
            {
                return;
            }

            _text = value;
            DebouncedUpdateDisplay();
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

    public void SetText(string text)
    {
        Text = text;
    }

    public void SetTransparentBackground(bool transparentBackground)
    {
        TransparentBackground = transparentBackground;
    }

    private void DebouncedUpdateDisplay()
    {
        // 取消之前的延迟任务
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();

        var token = _debounceCts.Token;

        // 延迟 150ms 后更新显示，避免频繁输入时过度渲染
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(150, token);
                if (!token.IsCancellationRequested)
                {
                    UpdateDisplay();
                }
            }
            catch (OperationCanceledException)
            {
                // 忽略取消异常
            }
        });
    }

    private void UpdateDisplay()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_text))
            {
                MarkdownViewer.Markdown = "*Empty*";
                return;
            }

            // 使用 Markdown 引擎渲染文本
            MarkdownViewer.Markdown = _text;
        }
        catch (Exception ex)
        {
            // 错误处理：显示错误信息而不是崩溃
            MarkdownViewer.Markdown = $"*Error: {ex.Message}*";
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

        // 设置最小和最大宽度
        RootBorder.MinWidth = cellSize * 1.5;
        RootBorder.MaxWidth = cellSize * 6;

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
}
