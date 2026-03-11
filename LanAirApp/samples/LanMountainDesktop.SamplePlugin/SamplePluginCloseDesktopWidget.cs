using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.SamplePlugin;

internal sealed class SamplePluginCloseDesktopWidget : Border
{
    private readonly PluginLocalizer _localizer;
    private readonly IHostApplicationLifecycle? _hostApplicationLifecycle;
    private readonly TextBlock _titleTextBlock;
    private readonly TextBlock _statusTextBlock;

    public SamplePluginCloseDesktopWidget(PluginDesktopComponentContext context)
    {
        _localizer = PluginLocalizer.Create(context);
        _hostApplicationLifecycle = context.GetService<IHostApplicationLifecycle>();

        _titleTextBlock = new TextBlock
        {
            Text = T("widget.close_desktop.text", "关闭桌面"),
            Foreground = Brushes.White,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        _statusTextBlock = new TextBlock
        {
            Text = _hostApplicationLifecycle is null
                ? T("widget.close_desktop.unavailable", "宿主未提供退出接口")
                : T("widget.close_desktop.hint", "点击后退出阑山桌面"),
            Foreground = new SolidColorBrush(Color.Parse("#FFD4E7F6")),
            VerticalAlignment = VerticalAlignment.Center
        };

        var contentGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                CreateIconShell(),
                new StackPanel
                {
                    Spacing = 2,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        _titleTextBlock,
                        _statusTextBlock
                    }
                }
            }
        };

        Grid.SetColumn(contentGrid.Children[1], 1);

        var actionButton = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            IsEnabled = _hostApplicationLifecycle is not null,
            Content = contentGrid
        };
        actionButton.Click += OnButtonClick;

        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.Parse("#FF0B1220"), 0),
                new GradientStop(Color.Parse("#FF172554"), 0.55),
                new GradientStop(Color.Parse("#FF7F1D1D"), 1)
            ]
        };
        BorderBrush = new SolidColorBrush(Color.Parse("#66FB7185"));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(18);
        Padding = new Thickness(14, 10);
        Child = actionButton;

        SizeChanged += OnSizeChanged;
        ApplyScale();
    }

    private Border CreateIconShell()
    {
        return new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(999),
            Background = new SolidColorBrush(Color.Parse("#33F87171")),
            BorderBrush = new SolidColorBrush(Color.Parse("#88FCA5A5")),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "⏻",
                FontSize = 18,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };
    }

    private void OnButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_hostApplicationLifecycle?.TryExit(new HostApplicationLifecycleRequest(
                Source: "SamplePlugin.CloseDesktopWidget",
                Reason: "User invoked the sample plugin close-desktop widget.")) == true)
        {
            return;
        }

        _statusTextBlock.Text = T("widget.close_desktop.failed", "宿主未接受退出请求");
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyScale();
    }

    private void ApplyScale()
    {
        var basis = Bounds.Height > 1 ? Bounds.Height : 72;
        Padding = new Thickness(Math.Clamp(basis * 0.18, 12, 18), Math.Clamp(basis * 0.14, 8, 14));
        CornerRadius = new CornerRadius(Math.Clamp(basis * 0.32, 16, 24));

        if (Child is not Button actionButton || actionButton.Content is not Grid contentGrid)
        {
            return;
        }

        if (contentGrid.Children[0] is Border iconShell)
        {
            var iconSize = Math.Clamp(basis * 0.58, 28, 40);
            iconShell.Width = iconSize;
            iconShell.Height = iconSize;
            if (iconShell.Child is TextBlock iconText)
            {
                iconText.FontSize = Math.Clamp(iconSize * 0.5, 14, 20);
            }
        }

        _titleTextBlock.FontSize = Math.Clamp(basis * 0.28, 14, 20);
        _statusTextBlock.FontSize = Math.Clamp(basis * 0.18, 10, 13);
    }

    private string T(string key, string fallback)
    {
        return _localizer.GetString(key, fallback);
    }
}
