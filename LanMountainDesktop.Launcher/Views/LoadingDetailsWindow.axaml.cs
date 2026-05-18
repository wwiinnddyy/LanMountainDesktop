using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.Launcher.Resources;
using LanMountainDesktop.Launcher.Services;
using LanMountainDesktop.Shared.Contracts.Launcher;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace LanMountainDesktop.Launcher.Views;

/// <summary>
/// 加载详情窗口 - 显示详细的加载状态和进度
/// </summary>
public partial class LoadingDetailsWindow : Window
{
    private readonly ObservableCollection<LoadingItemViewModel> _items = new();
    private readonly DispatcherTimer _updateTimer;
    private DateTimeOffset _startTime;

    public LoadingDetailsWindow()
    {
        AvaloniaXamlLoader.Load(this);

        var itemsList = this.FindControl<ItemsControl>("LoadingItemsList");
        if (itemsList != null)
        {
            itemsList.ItemsSource = _items;
        }

        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _updateTimer.Tick += OnUpdateTimerTick;

        _startTime = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 窗口加载完成
    /// </summary>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _updateTimer.Start();
    }

    /// <summary>
    /// 窗口关闭
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _updateTimer.Stop();
        base.OnClosing(e);
    }

    /// <summary>
    /// 更新加载状�?    /// </summary>
    public void UpdateLoadingState(LoadingStateMessage state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                // 更新标题和副标题
                UpdateHeader(state);

                // 更新整体进度
                UpdateOverallProgress(state);

                UpdateCurrentItem(state);

                // 更新列表
                UpdateItemsList(state);

                // 更新错误信息
                UpdateErrorPanel(state);

                // 更新完成计数
                UpdateCompletedCount(state);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[LoadingDetailsWindow] Error updating state: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 更新标题
    /// </summary>
    private void UpdateHeader(LoadingStateMessage state)
    {
        var subtitleText = this.FindControl<TextBlock>("SubtitleText");
        if (subtitleText != null)
        {
            subtitleText.Text = GetStageDescription(state.Stage);
        }
    }

    /// <summary>
    /// 更新整体进度
    /// </summary>
    private void UpdateOverallProgress(LoadingStateMessage state)
    {
        var progressBar = this.FindControl<ProgressBar>("OverallProgressBar");
        var percentText = this.FindControl<TextBlock>("PercentText");

        if (progressBar != null)
        {
            progressBar.Value = state.OverallProgressPercent;
        }

        if (percentText != null)
        {
            percentText.Text = $"{state.OverallProgressPercent}%";
        }
    }

    /// <summary>
    /// 更新当前活动�?    /// </summary>
    private void UpdateCurrentItem(LoadingStateMessage state)
    {
        var currentItem = state.ActiveItems.FirstOrDefault();
        if (currentItem == null) return;

        var nameText = this.FindControl<TextBlock>("CurrentItemName");
        var descText = this.FindControl<TextBlock>("CurrentItemDescription");
        var progressBar = this.FindControl<ProgressBar>("CurrentItemProgress");
        var iconText = this.FindControl<TextBlock>("CurrentItemIcon");

        if (nameText != null)
        {
            nameText.Text = currentItem.Name;
        }

        if (descText != null)
        {
            descText.Text = currentItem.Message ?? GetItemDescription(currentItem);
        }

        if (progressBar != null)
        {
            progressBar.Value = currentItem.ProgressPercent;
        }

        if (iconText != null)
        {
            iconText.Text = GetItemIcon(currentItem.Type);
        }
    }

    /// <summary>
    /// 更新列表
    /// </summary>
    private void UpdateItemsList(LoadingStateMessage state)
    {
        foreach (var item in state.ActiveItems)
        {
            var existing = _items.FirstOrDefault(i => i.Id == item.Id);
            if (existing != null)
            {
                existing.UpdateFrom(item);
            }
            else
            {
                _items.Add(new LoadingItemViewModel(item));
            }
        }

        // 移除已完成的项（保留最近完成的5个）
        var completedItems = _items.Where(i => i.State == LoadingState.Completed).ToList();
        if (completedItems.Count > 5)
        {
            var itemsToRemove = completedItems.OrderBy(i => i.CompletedTime).Take(completedItems.Count - 5);
            foreach (var item in itemsToRemove)
            {
                _items.Remove(item);
            }
        }

        // 按状态排序：进行�?-> 等待�?-> 已完�?-> 失败
        var sortedItems = _items.OrderBy(i => GetStatePriority(i.State)).ToList();
        _items.Clear();
        foreach (var item in sortedItems)
        {
            _items.Add(item);
        }
    }

    /// <summary>
    /// 更新错误面板
    /// </summary>
    private void UpdateErrorPanel(LoadingStateMessage state)
    {
        var errorPanel = this.FindControl<Border>("ErrorPanel");
        var errorText = this.FindControl<TextBlock>("ErrorText");

        if (errorPanel != null)
        {
            errorPanel.IsVisible = state.HasErrors;
        }

        if (errorText != null && state.ErrorMessages?.Any() == true)
        {
            errorText.Text = string.Join("\n", state.ErrorMessages.Take(3));
        }
    }

    /// <summary>
    /// 更新完成计数
    /// </summary>
    private void UpdateCompletedCount(LoadingStateMessage state)
    {
        var countText = this.FindControl<TextBlock>("CompletedCountText");
        if (countText != null)
        {
            countText.Text = state.CompletedCount.ToString();
        }
    }

    /// <summary>
    /// 定时更新
    /// </summary>
    private void OnUpdateTimerTick(object? sender, EventArgs e)
    {
        // 可以在这里添加时间显示等实时更新
    }

    /// <summary>
    /// 获取阶段描述
    /// </summary>
    private static string GetStageDescription(StartupStage stage) => stage switch
    {
        StartupStage.Initializing => "正在初始化系统...",
        StartupStage.LoadingSettings => "正在加载设置...",
        StartupStage.LoadingPlugins => "正在加载插件...",
        StartupStage.InitializingUI => "正在初始化界面...",
        StartupStage.ShellInitialized => "桌面程序已初始化",
        StartupStage.DesktopVisible => "桌面已经可见",
        StartupStage.ActivationRedirected => "已激活到现有实例",
        StartupStage.ActivationFailed => "激活现有实例失败",
        StartupStage.Ready => Strings.Loading_StageReady,
        _ => "正在加载..."
    };

    /// <summary>
    /// 获取项描�?    /// </summary>
    private static string GetItemDescription(LoadingItem item)
    {
        if (!string.IsNullOrEmpty(item.Description))
            return item.Description;

        return item.Type switch
        {
            LoadingItemType.Plugin => Strings.Loading_ItemPlugin,
            LoadingItemType.Component => Strings.Loading_ItemComponent,
            LoadingItemType.Resource => Strings.Loading_ItemResource,
            LoadingItemType.Data => Strings.Loading_ItemData,
            LoadingItemType.Network => Strings.Loading_ItemDownload,
            _ => Strings.Loading_ItemProcess
        };
    }

    /// <summary>
    /// 获取项图�?    /// </summary>
    private static string GetItemIcon(LoadingItemType type) => type switch
    {
        LoadingItemType.Plugin => "\uE768",
        LoadingItemType.Component => "\uE7C4",
        LoadingItemType.Resource => "\uE7C5",
        LoadingItemType.Data => "\uE7C6",
        LoadingItemType.Network => "\uE774",
        LoadingItemType.Settings => "\uE713",
        LoadingItemType.System => "\uE7C7",
        _ => "\uE768"
    };

    /// <summary>
    /// 获取状态优先级
    /// </summary>
    private static int GetStatePriority(LoadingState state) => state switch
    {
        LoadingState.InProgress => 0,
        LoadingState.Pending => 1,
        LoadingState.Completed => 2,
        LoadingState.Failed => 3,
        LoadingState.Timeout => 4,
        LoadingState.Cancelled => 5,
        _ => 6
    };
}

/// <summary>
/// 加载项视图模�?/// </summary>
public class LoadingItemViewModel : INotifyPropertyChanged
{
    public string Id { get; }
    public string Name { get; private set; }
    public LoadingItemType Type { get; private set; }
    public LoadingState State { get; private set; }
    public int ProgressPercent { get; private set; }
    public DateTimeOffset? CompletedTime { get; private set; }

    public string StatusIcon => GetStatusIcon(State);
    public IBrush StatusColor => GetStatusColor(State);
    public string ProgressText => State == LoadingState.Completed ? Strings.Loading_ItemComplete : $"{ProgressPercent}%";
    public string TypeLabel => GetTypeLabel(Type);
    public IBrush TypeBackground => GetTypeBackground(Type);
    public IBrush TypeForeground => GetTypeForeground(Type);
    public double Opacity => State == LoadingState.Completed ? 0.6 : 1.0;

    public event PropertyChangedEventHandler? PropertyChanged;

    public LoadingItemViewModel(LoadingItem item)
    {
        Id = item.Id;
        UpdateFrom(item);
    }

    public void UpdateFrom(LoadingItem item)
    {
        Name = item.Name;
        Type = item.Type;
        State = item.State;
        ProgressPercent = item.ProgressPercent;

        if (State == LoadingState.Completed && !CompletedTime.HasValue)
        {
            CompletedTime = DateTimeOffset.UtcNow;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    private static string GetStatusIcon(LoadingState state) => state switch
    {
        LoadingState.Pending => "\uE7C3",
        LoadingState.InProgress => "\uE768",
        LoadingState.Completed => "\uE73E",
        LoadingState.Failed => "\uE783",
        LoadingState.Timeout => "\uE71A",
        LoadingState.Cancelled => "\uE711",
        _ => "\uE7C3"
    };

    private static IBrush GetStatusColor(LoadingState state) => state switch
    {
        LoadingState.Pending => new SolidColorBrush(Colors.Gray),
        LoadingState.InProgress => new SolidColorBrush(Colors.DodgerBlue),
        LoadingState.Completed => new SolidColorBrush(Colors.Green),
        LoadingState.Failed => new SolidColorBrush(Colors.Red),
        LoadingState.Timeout => new SolidColorBrush(Colors.Orange),
        LoadingState.Cancelled => new SolidColorBrush(Colors.Gray),
        _ => new SolidColorBrush(Colors.Gray)
    };

    private static string GetTypeLabel(LoadingItemType type) => type switch
    {
        LoadingItemType.Plugin => Strings.Loading_TypePlugin,
        LoadingItemType.Component => Strings.Loading_TypeComponent,
        LoadingItemType.Resource => Strings.Loading_TypeResource,
        LoadingItemType.Data => Strings.Loading_TypeData,
        LoadingItemType.Network => Strings.Loading_TypeNetwork,
        LoadingItemType.Settings => Strings.Loading_TypeSettings,
        LoadingItemType.System => Strings.Loading_TypeSystem,
        _ => Strings.Loading_TypeOther
    };

    private static IBrush GetTypeBackground(LoadingItemType type) => type switch
    {
        LoadingItemType.Plugin => new SolidColorBrush(Color.Parse("#E3F2FD")),
        LoadingItemType.Component => new SolidColorBrush(Color.Parse("#F3E5F5")),
        LoadingItemType.Resource => new SolidColorBrush(Color.Parse("#E8F5E9")),
        LoadingItemType.Data => new SolidColorBrush(Color.Parse("#FFF3E0")),
        LoadingItemType.Network => new SolidColorBrush(Color.Parse("#E0F7FA")),
        _ => new SolidColorBrush(Color.Parse("#F5F5F5"))
    };

    private static IBrush GetTypeForeground(LoadingItemType type) => type switch
    {
        LoadingItemType.Plugin => new SolidColorBrush(Color.Parse("#1976D2")),
        LoadingItemType.Component => new SolidColorBrush(Color.Parse("#7B1FA2")),
        LoadingItemType.Resource => new SolidColorBrush(Color.Parse("#388E3C")),
        LoadingItemType.Data => new SolidColorBrush(Color.Parse("#F57C00")),
        LoadingItemType.Network => new SolidColorBrush(Color.Parse("#0097A7")),
        _ => new SolidColorBrush(Color.Parse("#616161"))
    };
}

