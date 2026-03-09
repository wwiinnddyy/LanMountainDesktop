using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.SamplePlugin;

internal sealed class SamplePluginStatusClockWidget : Border
{
    private readonly PluginDesktopComponentContext _context;
    private readonly PluginLocalizer _localizer;
    private readonly SamplePluginRuntimeStateService _stateService;
    private readonly SamplePluginClockService _clockService;
    private readonly IPluginMessageBus _messageBus;
    private readonly TextBlock _timeTextBlock;
    private readonly TextBlock _subtitleTextBlock;
    private readonly StackPanel _statusPanel;
    private readonly Border _statusHost;
    private readonly List<IDisposable> _subscriptions = [];
    private string? _instanceId;

    public SamplePluginStatusClockWidget(PluginDesktopComponentContext context)
    {
        _context = context;
        _localizer = PluginLocalizer.Create(context);
        _stateService = context.GetService<SamplePluginRuntimeStateService>()
            ?? throw new InvalidOperationException("SamplePluginRuntimeStateService is not available.");
        _clockService = context.GetService<SamplePluginClockService>()
            ?? throw new InvalidOperationException("SamplePluginClockService is not available.");
        _messageBus = context.GetService<IPluginMessageBus>()
            ?? throw new InvalidOperationException("IPluginMessageBus is not available.");

        _timeTextBlock = new TextBlock
        {
            Foreground = Brushes.White,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _subtitleTextBlock = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#FFBFE9FF")),
            HorizontalAlignment = HorizontalAlignment.Left,
            TextWrapping = TextWrapping.Wrap
        };
        _statusPanel = new StackPanel
        {
            Spacing = 8
        };
        _statusHost = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1F082F49")),
            BorderBrush = new SolidColorBrush(Color.Parse("#5538BDF8")),
            BorderThickness = new Thickness(1),
            Child = _statusPanel
        };

        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.Parse("#FF07111F"), 0),
                new GradientStop(Color.Parse("#FF0C4A6E"), 0.55),
                new GradientStop(Color.Parse("#FF0EA5E9"), 1)
            ]
        };
        BorderBrush = new SolidColorBrush(Color.Parse("#6648C7FF"));
        BorderThickness = new Thickness(1);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Child = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 14,
            Children =
            {
                new StackPanel
                {
                    Spacing = 4,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Children =
                    {
                        _timeTextBlock,
                        _subtitleTextBlock
                    }
                },
                _statusHost
            }
        };

        Grid.SetRow(((Grid)Child).Children[1], 1);

        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;

        RefreshClock(_clockService.CurrentTime);
        UpdateSubtitle();
        RefreshStatusPanel();
        ApplyScale();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_instanceId))
        {
            _instanceId = _stateService.RegisterComponentInstance(
                _context.ComponentId,
                _context.PlacementId,
                _context.CellSize);
        }

        _stateService.MarkFrontendReady(T(
            "status.frontend.detail.widget_connected",
            "组件界面已接入插件服务与通信。"));
        SubscribeToPluginBus();

        RefreshClock(_clockService.CurrentTime);
        UpdateSubtitle();
        RefreshStatusPanel();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();

        if (string.IsNullOrWhiteSpace(_instanceId))
        {
            return;
        }

        _stateService.UnregisterComponentInstance(_instanceId);
        _instanceId = null;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyScale();
        RefreshStatusPanel();
    }

    private void SubscribeToPluginBus()
    {
        if (_subscriptions.Count > 0)
        {
            return;
        }

        _subscriptions.Add(_messageBus.Subscribe<SamplePluginClockTickMessage>(message =>
            Dispatcher.UIThread.Post(() => RefreshClock(message.CurrentTime))));

        _subscriptions.Add(_messageBus.Subscribe<SamplePluginStateChangedMessage>(_ =>
            Dispatcher.UIThread.Post(() =>
            {
                UpdateSubtitle();
                RefreshStatusPanel();
            })));
    }

    private void RefreshClock(DateTimeOffset currentTime)
    {
        _timeTextBlock.Text = currentTime.LocalDateTime.ToString("HH:mm:ss");
    }

    private void UpdateSubtitle()
    {
        var snapshot = _stateService.GetSnapshot();
        _subtitleTextBlock.Text = string.IsNullOrWhiteSpace(_context.PlacementId)
            ? Tf("widget.subtitle.preview", "预览界面 | 已放置：{0}", snapshot.PlacedCount)
            : Tf("widget.subtitle.placement", "位置 {0} | 已放置：{1}", _context.PlacementId!, snapshot.PlacedCount);
    }

    private void RefreshStatusPanel()
    {
        _statusPanel.Children.Clear();

        var snapshot = _stateService.GetSnapshot();
        var basis = GetLayoutBasis();
        var titleSize = Math.Clamp(basis * 0.068, 11, 16);
        var detailSize = Math.Clamp(basis * 0.052, 9, 13);

        foreach (var entry in snapshot.StatusEntries)
        {
            var palette = GetPalette(entry.State);
            _statusPanel.Children.Add(new Border
            {
                Background = new SolidColorBrush(palette.Background),
                BorderBrush = new SolidColorBrush(palette.Border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 8),
                Child = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto"),
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                    ColumnSpacing = 8,
                    Children =
                    {
                        new Border
                        {
                            Width = Math.Clamp(basis * 0.038, 8, 11),
                            Height = Math.Clamp(basis * 0.038, 8, 11),
                            CornerRadius = new CornerRadius(999),
                            Background = new SolidColorBrush(palette.Dot),
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = entry.Title,
                            FontSize = titleSize,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = Brushes.White,
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = entry.Summary,
                            FontSize = detailSize,
                            Foreground = new SolidColorBrush(Color.Parse("#FFD7F2FF")),
                            HorizontalAlignment = HorizontalAlignment.Right,
                            TextAlignment = TextAlignment.Right,
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = entry.Detail,
                            FontSize = detailSize,
                            Foreground = new SolidColorBrush(Color.Parse("#FFD7F2FF")),
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            });

            var row = (Grid)((Border)_statusPanel.Children[^1]).Child!;
            Grid.SetColumn(row.Children[1], 1);
            Grid.SetColumn(row.Children[2], 2);
            Grid.SetColumnSpan(row.Children[3], 3);
            Grid.SetRow(row.Children[3], 1);
        }
    }

    private void ApplyScale()
    {
        var basis = GetLayoutBasis();
        Padding = new Thickness(Math.Clamp(basis * 0.09, 16, 26));
        CornerRadius = new CornerRadius(Math.Clamp(basis * 0.14, 20, 34));
        _timeTextBlock.FontSize = Math.Clamp(basis * 0.22, 30, 58);
        _subtitleTextBlock.FontSize = Math.Clamp(basis * 0.062, 11, 17);
        _statusHost.Padding = new Thickness(Math.Clamp(basis * 0.045, 10, 18));
        _statusHost.CornerRadius = new CornerRadius(Math.Clamp(basis * 0.09, 14, 22));
        _statusPanel.Spacing = Math.Clamp(basis * 0.024, 6, 10);
    }

    private double GetLayoutBasis()
    {
        var width = Bounds.Width > 1 ? Bounds.Width : _context.CellSize * 4;
        var height = Bounds.Height > 1 ? Bounds.Height : _context.CellSize * 4;
        return Math.Max(_context.CellSize * 4, Math.Min(width, height));
    }

    private static (Color Background, Color Border, Color Dot) GetPalette(SamplePluginHealthState state)
    {
        return state switch
        {
            SamplePluginHealthState.Healthy => (
                Color.Parse("#1F0F766E"),
                Color.Parse("#4D5EEAD4"),
                Color.Parse("#5EEAD4")),
            SamplePluginHealthState.Faulted => (
                Color.Parse("#29B91C1C"),
                Color.Parse("#66F87171"),
                Color.Parse("#F87171")),
            _ => (
                Color.Parse("#1F7C2D12"),
                Color.Parse("#66FDBA74"),
                Color.Parse("#FDBA74"))
        };
    }

    private string T(string key, string fallback)
    {
        return _localizer.GetString(key, fallback);
    }

    private string Tf(string key, string fallback, params object[] args)
    {
        return _localizer.Format(key, fallback, args);
    }
}
