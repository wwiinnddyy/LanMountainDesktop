using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.SamplePlugin;

internal sealed class SamplePluginStatusClockWidget : Border
{
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    private readonly PluginDesktopComponentContext _context;
    private readonly TextBlock _timeTextBlock;
    private readonly TextBlock _titleTextBlock;
    private readonly StackPanel _statusPanel;

    public SamplePluginStatusClockWidget(PluginDesktopComponentContext context)
    {
        _context = context;
        _timeTextBlock = new TextBlock
        {
            Foreground = Brushes.White,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _titleTextBlock = new TextBlock
        {
            Text = "Plugin Status",
            Foreground = new SolidColorBrush(Color.Parse("#FFBFE9FF")),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _statusPanel = new StackPanel
        {
            Spacing = 8
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
                        _titleTextBlock
                    }
                },
                new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#1F082F49")),
                    BorderBrush = new SolidColorBrush(Color.Parse("#5538BDF8")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(18),
                    Padding = new Thickness(12),
                    Child = _statusPanel
                }
            }
        };

        Grid.SetRow(((Grid)Child).Children[1], 1);

        _timer.Tick += OnTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;

        var placementText = string.IsNullOrWhiteSpace(context.PlacementId)
            ? "Preview instance created."
            : $"Widget created for placement {context.PlacementId}.";
        SamplePluginRuntimeStatus.MarkFrontendReady("Widget frontend surface rendered successfully.");
        SamplePluginRuntimeStatus.MarkComponentCreated($"{placementText} Baseline footprint: 4x4.");

        RefreshClock();
        RefreshStatusPanel();
        ApplyScale();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        RefreshClock();
        RefreshStatusPanel();
        _timer.Start();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        RefreshClock();
        RefreshStatusPanel();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyScale();
        RefreshStatusPanel();
    }

    private void RefreshClock()
    {
        _timeTextBlock.Text = DateTime.Now.ToString("HH:mm:ss");
    }

    private void RefreshStatusPanel()
    {
        _statusPanel.Children.Clear();

        var basis = GetLayoutBasis();
        var titleSize = Math.Clamp(basis * 0.072, 11, 16);
        var detailSize = Math.Clamp(basis * 0.055, 10, 13);

        foreach (var entry in SamplePluginRuntimeStatus.GetSnapshot())
        {
            var palette = GetPalette(entry.State);
            var summaryText = $"{entry.Summary} - {entry.UpdatedAt.LocalDateTime:HH:mm:ss}";

            _statusPanel.Children.Add(new Border
            {
                Background = new SolidColorBrush(palette.Background),
                BorderBrush = new SolidColorBrush(palette.Border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 8),
                Child = new Grid
                {
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
                            Text = summaryText,
                            FontSize = detailSize,
                            Foreground = new SolidColorBrush(Color.Parse("#FFD7F2FF")),
                            HorizontalAlignment = HorizontalAlignment.Right,
                            TextAlignment = TextAlignment.Right,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                }
            });

            var row = (Grid)((Border)_statusPanel.Children[^1]).Child!;
            Grid.SetColumn(row.Children[1], 1);
            Grid.SetColumn(row.Children[2], 2);
        }
    }

    private void ApplyScale()
    {
        var basis = GetLayoutBasis();
        Padding = new Thickness(Math.Clamp(basis * 0.09, 16, 26));
        CornerRadius = new CornerRadius(Math.Clamp(basis * 0.14, 20, 34));
        _timeTextBlock.FontSize = Math.Clamp(basis * 0.22, 30, 58);
        _titleTextBlock.FontSize = Math.Clamp(basis * 0.07, 12, 18);
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
}
