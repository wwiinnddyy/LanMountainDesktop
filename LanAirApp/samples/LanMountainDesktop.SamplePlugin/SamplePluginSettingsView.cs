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
    private readonly PluginLocalizer _localizer;
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
        _localizer = PluginLocalizer.Create(context);
        _stateService = context.GetService<SamplePluginRuntimeStateService>()
            ?? throw new InvalidOperationException("SamplePluginRuntimeStateService is not available.");
        _clockService = context.GetService<SamplePluginClockService>()
            ?? throw new InvalidOperationException("SamplePluginClockService is not available.");
        _messageBus = context.GetService<IPluginMessageBus>()
            ?? throw new InvalidOperationException("IPluginMessageBus is not available.");

        _stateService.MarkFrontendReady(T(
            "status.frontend.detail.settings_connected",
            "设置页已接入插件服务与通信。"));

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
                        Text = T("settings.header.title", "示例插件能力检查器"),
                        FontSize = 22,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brushes.White
                    },
                    CreateSection(T("settings.section.info", "插件信息"), _pluginInfoPanel),
                    CreateSection(T("settings.section.capabilities", "可访问能力"), _capabilityPanel),
                    CreateSection(T("settings.section.status", "实时运行状态"), _statusPanel)
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
        _pluginInfoPanel.Children.Add(CreateInfoLine(
            T("settings.info.plugin_name", "插件名称"),
            T("plugin.name", snapshot.Manifest.Name)));
        _pluginInfoPanel.Children.Add(CreateInfoLine(T("settings.info.plugin_id", "插件 Id"), snapshot.Manifest.Id));
        _pluginInfoPanel.Children.Add(CreateInfoLine(T("settings.info.version", "版本"), snapshot.Manifest.Version ?? T("common.dev", "开发版")));
        _pluginInfoPanel.Children.Add(CreateInfoLine(T("settings.info.author", "作者"), snapshot.Manifest.Author ?? T("common.none", "（无）")));
        _pluginInfoPanel.Children.Add(CreateInfoLine(
            T("settings.info.description", "描述"),
            T("plugin.description", snapshot.Manifest.Description ?? T("common.none", "（无）"))));
        _pluginInfoPanel.Children.Add(CreateInfoLine(T("settings.info.plugin_directory", "插件目录"), snapshot.PluginDirectory));
        _pluginInfoPanel.Children.Add(CreateInfoLine(T("settings.info.data_directory", "数据目录"), snapshot.DataDirectory));
        _pluginInfoPanel.Children.Add(CreateInfoLine(T("settings.info.host_application", "宿主应用"), snapshot.HostApplicationName));
        _pluginInfoPanel.Children.Add(CreateInfoLine(T("settings.info.host_version", "宿主版本"), snapshot.HostVersion));
        _pluginInfoPanel.Children.Add(CreateInfoLine(T("settings.info.sdk_api_version", "SDK API 版本"), snapshot.SdkApiVersion));
        _pluginInfoPanel.Children.Add(CreateInfoLine(
            T("settings.info.state_service_resolved", "状态服务已解析"),
            FormatBoolean(_context.GetService<SamplePluginRuntimeStateService>() is not null)));
        _pluginInfoPanel.Children.Add(CreateInfoLine(
            T("settings.info.clock_service_resolved", "时钟服务已解析"),
            FormatBoolean(_context.GetService<SamplePluginClockService>() is not null)));
        _pluginInfoPanel.Children.Add(CreateInfoLine(
            T("settings.info.message_bus_resolved", "消息总线已解析"),
            FormatBoolean(_context.GetService<IPluginMessageBus>() is not null)));
        _pluginInfoPanel.Children.Add(CreateInfoLine(
            T("settings.info.component_placed", "组件是否已放置"),
            snapshot.HasPlacedComponent ? T("common.yes", "是") : T("common.no", "否")));
        _pluginInfoPanel.Children.Add(CreateInfoLine(T("settings.info.placed_count", "已放置数量"), snapshot.PlacedCount.ToString()));
        _pluginInfoPanel.Children.Add(CreateInfoLine(T("settings.info.preview_count", "预览数量"), snapshot.PreviewCount.ToString()));
        _pluginInfoPanel.Children.Add(CreateInfoLine(
            T("settings.info.placement_ids", "放置位置 Id"),
            snapshot.PlacementIds.Count == 0 ? T("common.none", "（无）") : string.Join(", ", snapshot.PlacementIds)));
        _pluginInfoPanel.Children.Add(CreateInfoLine(
            T("settings.info.last_component_id", "最近组件 Id"),
            snapshot.LastComponentId ?? T("common.none", "（无）")));
        _pluginInfoPanel.Children.Add(CreateInfoLine(
            T("settings.info.last_cell_size", "最近单元尺寸"),
            snapshot.LastCellSize > 0 ? $"{snapshot.LastCellSize:F0}px" : T("common.unknown", "（未知）")));
        _pluginInfoPanel.Children.Add(CreateInfoLine(
            T("settings.info.clock_service_time", "时钟服务时间"),
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
                            Text = Tf("settings.status.updated_at", "更新时间：{0}", entry.UpdatedAt.LocalDateTime.ToString("HH:mm:ss")),
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

    private string T(string key, string fallback)
    {
        return _localizer.GetString(key, fallback);
    }

    private string Tf(string key, string fallback, params object[] args)
    {
        return _localizer.Format(key, fallback, args);
    }

    private string FormatBoolean(bool value)
    {
        return value
            ? T("common.true", "是")
            : T("common.false", "否");
    }
}
