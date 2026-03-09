using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.SamplePlugin;

internal sealed class SamplePluginSettingsView : UserControl
{
    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    private readonly IPluginContext _context;
    private readonly TextBlock _summaryTextBlock;
    private readonly StackPanel _statusPanel;

    public SamplePluginSettingsView(IPluginContext context)
    {
        _context = context;
        _summaryTextBlock = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#FFBAE6FD")),
            TextWrapping = TextWrapping.Wrap
        };
        _statusPanel = new StackPanel
        {
            Spacing = 10
        };

        SamplePluginRuntimeStatus.MarkFrontendReady("Settings page rendered successfully.");

        _refreshTimer.Tick += OnRefreshTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;

        Content = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                [
                    new GradientStop(Color.Parse("#1F0B1120"), 0),
                    new GradientStop(Color.Parse("#260C4A6E"), 1)
                ]
            },
            BorderBrush = new SolidColorBrush(Color.Parse("#6628B2FF")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Sample Plugin Runtime Status",
                        FontSize = 22,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brushes.White
                    },
                    _summaryTextBlock,
                    new Border
                    {
                        Background = new SolidColorBrush(Color.Parse("#14000000")),
                        BorderBrush = new SolidColorBrush(Color.Parse("#3328B2FF")),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(14),
                        Padding = new Thickness(14),
                        Child = _statusPanel
                    }
                }
            }
        };

        RefreshStatuses();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        RefreshStatuses();
        _refreshTimer.Start();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _refreshTimer.Stop();
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        RefreshStatuses();
    }

    private void RefreshStatuses()
    {
        _summaryTextBlock.Text =
            $"Plugin Id: {_context.Manifest.Id}\nVersion: {_context.Manifest.Version ?? "dev"}\nData Path: {_context.DataDirectory}";

        _statusPanel.Children.Clear();
        foreach (var entry in SamplePluginRuntimeStatus.GetSnapshot())
        {
            var palette = GetPalette(entry.State);
            _statusPanel.Children.Add(new Border
            {
                Background = new SolidColorBrush(palette.Background),
                BorderBrush = new SolidColorBrush(palette.Border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 10),
                Child = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                            ColumnSpacing = 8,
                            Children =
                            {
                                new Border
                                {
                                    Width = 10,
                                    Height = 10,
                                    CornerRadius = new CornerRadius(999),
                                    Background = new SolidColorBrush(palette.Dot),
                                    VerticalAlignment = VerticalAlignment.Center
                                },
                                new TextBlock
                                {
                                    Text = entry.Title,
                                    FontSize = 15,
                                    FontWeight = FontWeight.SemiBold,
                                    Foreground = Brushes.White
                                },
                                new TextBlock
                                {
                                    Text = entry.Summary,
                                    Foreground = new SolidColorBrush(Color.Parse("#FFD7F2FF")),
                                    HorizontalAlignment = HorizontalAlignment.Right
                                }
                            }
                        },
                        new TextBlock
                        {
                            Text = entry.Detail,
                            Foreground = new SolidColorBrush(Color.Parse("#FFE0F2FE")),
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = $"Updated: {entry.UpdatedAt.LocalDateTime:HH:mm:ss}",
                            Foreground = new SolidColorBrush(Color.Parse("#FF93C5FD"))
                        }
                    }
                }
            });

            var row = (Grid)((StackPanel)((Border)_statusPanel.Children[^1]).Child!).Children[0];
            Grid.SetColumn(row.Children[1], 1);
            Grid.SetColumn(row.Children[2], 2);
        }
    }

    private static (Color Background, Color Border, Color Dot) GetPalette(SamplePluginHealthState state)
    {
        return state switch
        {
            SamplePluginHealthState.Healthy => (
                Color.Parse("#1F115E59"),
                Color.Parse("#665EEAD4"),
                Color.Parse("#5EEAD4")),
            SamplePluginHealthState.Faulted => (
                Color.Parse("#291B1B"),
                Color.Parse("#66F87171"),
                Color.Parse("#F87171")),
            _ => (
                Color.Parse("#2B3A2A0D"),
                Color.Parse("#66FBBF24"),
                Color.Parse("#FBBF24"))
        };
    }
}
