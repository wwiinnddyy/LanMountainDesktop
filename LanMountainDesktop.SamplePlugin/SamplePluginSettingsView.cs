using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.SamplePlugin;

internal sealed class SamplePluginSettingsView : UserControl
{
    private readonly IPluginContext _context;
    private readonly SamplePluginRuntimeStateService _stateService;
    private readonly SamplePluginClockService _clockService;
    private readonly IPluginMessageBus _messageBus;
    private readonly StackPanel _pluginInfoPanel = new() { Spacing = 8 };
    private readonly StackPanel _capabilityPanel = new() { Spacing = 8 };
    private readonly StackPanel _statusPanel = new() { Spacing = 10 };
    private readonly List<IDisposable> _subscriptions = [];

    public SamplePluginSettingsView(IPluginContext context)
    {
        _context = context;
        _stateService = context.GetService<SamplePluginRuntimeStateService>()
            ?? throw new InvalidOperationException("SamplePluginRuntimeStateService is not available.");
        _clockService = context.GetService<SamplePluginClockService>()
            ?? throw new InvalidOperationException("SamplePluginClockService is not available.");
        _messageBus = context.GetService<IPluginMessageBus>()
            ?? throw new InvalidOperationException("IPluginMessageBus is not available.");

        _stateService.MarkFrontendReady("Settings page is connected to plugin services and communication.");

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
                        Text = "Sample Plugin Capability Inspector",
                        FontSize = 22,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brushes.White
                    },
                    CreateSection("Plugin Info", _pluginInfoPanel),
                    CreateSection("Accessible Capabilities", _capabilityPanel),
                    CreateSection("Live Runtime Status", _statusPanel)
                }
            }
        };

        RefreshView();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        SubscribeToPluginBus();
        RefreshView();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    private void SubscribeToPluginBus()
    {
        if (_subscriptions.Count > 0)
        {
            return;
        }

        _subscriptions.Add(_messageBus.Subscribe<SamplePluginClockTickMessage>(_ =>
            Dispatcher.UIThread.Post(RefreshView)));

        _subscriptions.Add(_messageBus.Subscribe<SamplePluginStateChangedMessage>(_ =>
            Dispatcher.UIThread.Post(RefreshView)));
    }

    private void RefreshView()
    {
        var snapshot = _stateService.GetSnapshot();
        RefreshPluginInfo(snapshot);
        RefreshCapabilities();
        RefreshStatuses(snapshot);
    }

    private void RefreshPluginInfo(SamplePluginRuntimeSnapshot snapshot)
    {
        _pluginInfoPanel.Children.Clear();
        _pluginInfoPanel.Children.Add(CreateInfoLine("Plugin Name", snapshot.Manifest.Name));
        _pluginInfoPanel.Children.Add(CreateInfoLine("Plugin Id", snapshot.Manifest.Id));
        _pluginInfoPanel.Children.Add(CreateInfoLine("Version", snapshot.Manifest.Version ?? "dev"));
        _pluginInfoPanel.Children.Add(CreateInfoLine("Author", snapshot.Manifest.Author ?? "(none)"));
        _pluginInfoPanel.Children.Add(CreateInfoLine("Description", snapshot.Manifest.Description ?? "(none)"));
        _pluginInfoPanel.Children.Add(CreateInfoLine("Plugin Directory", snapshot.PluginDirectory));
        _pluginInfoPanel.Children.Add(CreateInfoLine("Data Directory", snapshot.DataDirectory));
        _pluginInfoPanel.Children.Add(CreateInfoLine("Host Application", snapshot.HostApplicationName));
        _pluginInfoPanel.Children.Add(CreateInfoLine("Host Version", snapshot.HostVersion));
        _pluginInfoPanel.Children.Add(CreateInfoLine("SDK API Version", snapshot.SdkApiVersion));
        _pluginInfoPanel.Children.Add(CreateInfoLine("State Service Resolved", (_context.GetService<SamplePluginRuntimeStateService>() is not null).ToString()));
        _pluginInfoPanel.Children.Add(CreateInfoLine("Clock Service Resolved", (_context.GetService<SamplePluginClockService>() is not null).ToString()));
        _pluginInfoPanel.Children.Add(CreateInfoLine("Message Bus Resolved", (_context.GetService<IPluginMessageBus>() is not null).ToString()));
        _pluginInfoPanel.Children.Add(CreateInfoLine("Component Placed", snapshot.HasPlacedComponent ? "Yes" : "No"));
        _pluginInfoPanel.Children.Add(CreateInfoLine("Placed Count", snapshot.PlacedCount.ToString()));
        _pluginInfoPanel.Children.Add(CreateInfoLine("Preview Count", snapshot.PreviewCount.ToString()));
        _pluginInfoPanel.Children.Add(CreateInfoLine(
            "Placement Ids",
            snapshot.PlacementIds.Count == 0 ? "(none)" : string.Join(", ", snapshot.PlacementIds)));
        _pluginInfoPanel.Children.Add(CreateInfoLine("Last Component Id", snapshot.LastComponentId ?? "(none)"));
        _pluginInfoPanel.Children.Add(CreateInfoLine(
            "Last Cell Size",
            snapshot.LastCellSize > 0 ? $"{snapshot.LastCellSize:F0}px" : "(unknown)"));
        _pluginInfoPanel.Children.Add(CreateInfoLine(
            "Clock Service Time",
            _clockService.CurrentTime.LocalDateTime.ToString("HH:mm:ss")));
    }

    private void RefreshCapabilities()
    {
        var capabilities = _stateService.GetCapabilities(
            _context,
            _context.GetService<SamplePluginRuntimeStateService>() is not null,
            _context.GetService<SamplePluginClockService>() is not null,
            _context.GetService<IPluginMessageBus>() is not null);

        _capabilityPanel.Children.Clear();
        foreach (var capability in capabilities)
        {
            _capabilityPanel.Children.Add(CreateCapabilityCard(capability));
        }
    }

    private void RefreshStatuses(SamplePluginRuntimeSnapshot snapshot)
    {
        _statusPanel.Children.Clear();

        foreach (var entry in snapshot.StatusEntries)
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
                        CreateStatusHeader(entry, palette),
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
        }
    }

    private Border CreateSection(string title, Control content)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#14000000")),
            BorderBrush = new SolidColorBrush(Color.Parse("#3328B2FF")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 16,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brushes.White
                    },
                    content
                }
            }
        };
    }

    private Control CreateInfoLine(string label, string value)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("180,*"),
            ColumnSpacing = 10
        };

        var labelText = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.Parse("#FFBAE6FD")),
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        var valueText = new TextBlock
        {
            Text = value,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap
        };

        grid.Children.Add(labelText);
        grid.Children.Add(valueText);
        Grid.SetColumn(valueText, 1);
        return grid;
    }

    private Control CreateCapabilityCard(SamplePluginCapabilityItem item)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#0F082F49")),
            BorderBrush = new SolidColorBrush(Color.Parse("#3338BDF8")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 10),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock
                    {
                        Text = item.Title,
                        Foreground = Brushes.White,
                        FontWeight = FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text = item.Detail,
                        Foreground = new SolidColorBrush(Color.Parse("#FFE0F2FE")),
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };
    }

    private static Control CreateStatusHeader(
        SamplePluginStatusEntry entry,
        (Color Background, Color Border, Color Dot) palette)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 8
        };

        var dot = new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(999),
            Background = new SolidColorBrush(palette.Dot),
            VerticalAlignment = VerticalAlignment.Center
        };
        var title = new TextBlock
        {
            Text = entry.Title,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White
        };
        var summary = new TextBlock
        {
            Text = entry.Summary,
            Foreground = new SolidColorBrush(Color.Parse("#FFD7F2FF")),
            HorizontalAlignment = HorizontalAlignment.Right
        };

        grid.Children.Add(dot);
        grid.Children.Add(title);
        grid.Children.Add(summary);
        Grid.SetColumn(title, 1);
        Grid.SetColumn(summary, 2);
        return grid;
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
