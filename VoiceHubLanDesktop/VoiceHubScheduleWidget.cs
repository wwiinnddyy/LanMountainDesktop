using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.PluginSdk;

namespace VoiceHubLanDesktop;

/// <summary>
/// 广播站排期显示组件
/// </summary>
internal sealed class VoiceHubScheduleWidget : Border
{
    private readonly PluginDesktopComponentContext _context;
    private readonly PluginLocalizer _localizer;
    private readonly VoiceHubScheduleService _scheduleService;
    private readonly PluginAppearanceSnapshot? _appearanceSnapshot;
    private readonly TextBlock _titleTextBlock;
    private readonly TextBlock _dateTextBlock;
    private readonly StackPanel _contentPanel;
    private readonly StackPanel _loadingPanel;
    private readonly StackPanel _errorPanel;
    private readonly DispatcherTimer? _refreshTimer;
    private CancellationTokenSource? _loadCts;

    public VoiceHubScheduleWidget(PluginDesktopComponentContext context)
    {
        _context = context;
        _localizer = PluginLocalizer.Create(context);
        _scheduleService = context.GetService<VoiceHubScheduleService>()
            ?? throw new InvalidOperationException("VoiceHubScheduleService is not available.");
        _appearanceSnapshot = context.GetAppearanceSnapshot();

        // 创建 UI 元素
        _titleTextBlock = new TextBlock
        {
            Foreground = Brushes.White,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };

        _dateTextBlock = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#FFBFE9FF")),
            VerticalAlignment = VerticalAlignment.Center
        };

        _contentPanel = new StackPanel
        {
            Spacing = 8
        };

        _loadingPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 12,
            Children =
            {
                new ProgressBar
                {
                    IsIndeterminate = true,
                    Width = 100,
                    Height = 4
                },
                new TextBlock
                {
                    Text = T("widget.loading", "正在加载排期..."),
                    Foreground = new SolidColorBrush(Color.Parse("#FFBFE9FF"))
                }
            }
        };

        _errorPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 8
        };

        // 设置背景和边框
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

        // 构建主布局
        Child = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 12,
            Children =
            {
                // 标题栏
                new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#1F082F49")),
                    BorderBrush = new SolidColorBrush(Color.Parse("#5538BDF8")),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(12, 8),
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "📻",
                                FontSize = 16,
                                VerticalAlignment = VerticalAlignment.Center
                            },
                            _titleTextBlock,
                            _dateTextBlock
                        }
                    }
                },
                // 内容区域
                new ScrollViewer
                {
                    Padding = new Thickness(8),
                    Content = _contentPanel
                }
            }
        };

        Grid.SetRow(((Grid)Child).Children[1], 1);

        // 设置刷新定时器
        var refreshInterval = GetRefreshInterval();
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(refreshInterval)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();

        // 事件处理
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;

        // 初始化显示
        SetTitle();
        ApplyScale();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _refreshTimer?.Start();
        _ = LoadAsync();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _refreshTimer?.Stop();
        _loadCts?.Cancel();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyScale();
    }

    private async Task LoadAsync()
    {
        ShowLoading();

        try
        {
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();

            var displayData = await _scheduleService.GetTodayScheduleAsync(_loadCts.Token);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ApplyDisplayData(displayData);
            });
        }
        catch (OperationCanceledException)
        {
            // 忽略取消
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ShowError($"加载失败: {ex.Message}");
            });
        }
    }

    private void ApplyDisplayData(DisplayData data)
    {
        switch (data.State)
        {
            case ComponentState.Normal:
                ShowContent(data);
                break;
            case ComponentState.NoSchedule:
                ShowError(data.ErrorMessage ?? "暂无排期数据");
                break;
            case ComponentState.NetworkError:
                ShowError(data.ErrorMessage ?? "网络错误");
                break;
        }
    }

    private void ShowLoading()
    {
        if (Child is not Grid mainGrid) return;
        mainGrid.Children[1] = _loadingPanel;
    }

    private void ShowError(string message)
    {
        _errorPanel.Children.Clear();
        _errorPanel.Children.Add(new TextBlock
        {
            Text = "⚠️",
            FontSize = 48,
            Foreground = new SolidColorBrush(Color.Parse("#FFF87171"))
        });
        _errorPanel.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(Color.Parse("#FFF87171")),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 200,
            TextAlignment = TextAlignment.Center
        });
        _errorPanel.Children.Add(new Button
        {
            Content = T("widget.retry", "重试"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        });

        var retryButton = (Button)_errorPanel.Children[2];
        retryButton.Click += async (_, _) => await RefreshAsync();

        if (Child is not Grid mainGrid) return;
        mainGrid.Children[1] = _errorPanel;
    }

    private void ShowContent(DisplayData data)
    {
        _contentPanel.Children.Clear();

        var basis = GetLayoutBasis();
        var titleSize = Math.Clamp(basis * 0.055, 12, 16);
        var detailSize = Math.Clamp(basis * 0.045, 10, 13);

        foreach (var item in data.Songs)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#1F082F49")),
                BorderBrush = new SolidColorBrush(Color.Parse("#5538BDF8")),
                BorderThickness = new Thickness(1),
                CornerRadius = _appearanceSnapshot.ResolveCornerRadius(
                    PluginCornerRadiusPreset.Md,
                    new CornerRadius(8)),
                Padding = new Thickness(12, 10),
                Child = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                    ColumnSpacing = 12,
                    Children =
                    {
                        // 序号
                        new Border
                        {
                            Width = 24,
                            Height = 24,
                            CornerRadius = new CornerRadius(12),
                            Background = new SolidColorBrush(Color.Parse("#FF0EA5E9")),
                            VerticalAlignment = VerticalAlignment.Center,
                            Child = new TextBlock
                            {
                                Text = item.Sequence.ToString(),
                                FontSize = 11,
                                FontWeight = FontWeight.Bold,
                                Foreground = Brushes.White,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center
                            }
                        },
                        // 歌曲信息
                        new StackPanel
                        {
                            Spacing = 4,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = item.Song.Title,
                                    FontSize = titleSize,
                                    FontWeight = FontWeight.Medium,
                                    Foreground = Brushes.White,
                                    TextTrimming = TextTrimming.CharacterEllipsis,
                                    MaxLines = 1
                                },
                                new TextBlock
                                {
                                    Text = $"{item.Song.Artist}",
                                    FontSize = detailSize,
                                    Foreground = new SolidColorBrush(Color.Parse("#FFBFE9FF")),
                                    TextTrimming = TextTrimming.CharacterEllipsis,
                                    MaxLines = 1
                                }
                            }
                        }
                    }
                }
            };

            Grid.SetColumn(((Grid)card.Child!).Children[1], 1);
            _contentPanel.Children.Add(card);
        }

        // 更新日期显示
        _dateTextBlock.Text = data.DisplayDate?.ToString("MM月dd日") ?? "";

        if (Child is not Grid mainGrid) return;
        mainGrid.Children[1] = new ScrollViewer
        {
            Padding = new Thickness(8),
            Content = _contentPanel
        };
    }

    private void SetTitle()
    {
        _titleTextBlock.Text = T("widget.display_name", "广播站排期");
    }

    private void ApplyScale()
    {
        var basis = GetLayoutBasis();
        Padding = new Thickness(Math.Clamp(basis * 0.06, 10, 18));
        CornerRadius = _appearanceSnapshot.ResolveCornerRadius(
            PluginCornerRadiusPreset.Island,
            new CornerRadius(Math.Clamp(basis * 0.12, 16, 28)));
        _titleTextBlock.FontSize = Math.Clamp(basis * 0.065, 12, 16);
        _dateTextBlock.FontSize = Math.Clamp(basis * 0.05, 10, 13);
    }

    private double GetLayoutBasis()
    {
        var width = Bounds.Width > 1 ? Bounds.Width : _context.CellSize * 3;
        var height = Bounds.Height > 1 ? Bounds.Height : _context.CellSize * 4;
        return Math.Max(_context.CellSize * 3, Math.Min(width, height));
    }

    private int GetRefreshInterval()
    {
        try
        {
            var interval = _context.GetService<IPluginSettingsService>()
                ?.GetValue<string>(SettingsScope.Plugin, "refreshInterval", sectionId: "voicehub-settings");
            if (!string.IsNullOrWhiteSpace(interval) && int.TryParse(interval, out var minutes))
            {
                return minutes;
            }
        }
        catch { }
        return 60;
    }

    public async Task RefreshAsync()
    {
        _scheduleService.ClearCache();
        await LoadAsync();
    }

    private string T(string key, string fallback)
    {
        return _localizer.GetString(key, fallback);
    }
}
