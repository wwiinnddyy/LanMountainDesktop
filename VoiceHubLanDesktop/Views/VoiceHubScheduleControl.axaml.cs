using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LanMountainDesktop.PluginSdk;
using VoiceHubLanDesktop.Models;
using VoiceHubLanDesktop.Services;

namespace VoiceHubLanDesktop.Views;

/// <summary>
/// 广播站排期显示组件
/// </summary>
public sealed partial class VoiceHubScheduleControl : UserControl
{
    private readonly VoiceHubScheduleService _scheduleService;
    private readonly VoiceHubSettingsService _settingsService;
    private readonly DispatcherTimer? _refreshTimer;
    private CancellationTokenSource? _loadCts;

    public ObservableCollection<SongItem> Songs { get; } = [];

    [ObservableProperty] private string _titleText = "广播站排期";
    [ObservableProperty] private string _dateText = "";
    [ObservableProperty] private string _emptyMessage = "暂无排期数据";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _isNormal = false;
    [ObservableProperty] private bool _isEmpty = false;
    [ObservableProperty] private bool _isError = false;

    public VoiceHubScheduleControl(
        VoiceHubScheduleService scheduleService,
        VoiceHubSettingsService settingsService,
        IPluginRuntimeContext runtimeContext)
    {
        InitializeComponent();
        DataContext = this;

        _scheduleService = scheduleService;
        _settingsService = settingsService;

        // 设置刷新定时器
        var settings = _settingsService.GetSettings();
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(settings.RefreshIntervalMinutes)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();

        // 监听设置变化
        _settingsService.SettingsChanged += OnSettingsChanged;

        // 初始加载
        _ = LoadAsync();
    }

    private void OnSettingsChanged(object? sender, PluginSettings settings)
    {
        if (_refreshTimer != null)
        {
            _refreshTimer.Interval = TimeSpan.FromMinutes(settings.RefreshIntervalMinutes);
        }
        _scheduleService.ClearCache();
        _ = RefreshAsync();
    }

    private async Task LoadAsync()
    {
        SetState(ComponentState.Loading);

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
                SetState(ComponentState.NetworkError, $"加载失败: {ex.Message}");
            });
        }
    }

    private void ApplyDisplayData(DisplayData data)
    {
        switch (data.State)
        {
            case ComponentState.Normal:
                Songs.Clear();
                foreach (var song in data.Songs)
                {
                    Songs.Add(song);
                }
                DateText = data.DisplayDate?.ToString("MM月dd日") ?? "";
                SetState(ComponentState.Normal);
                break;

            case ComponentState.NoSchedule:
                EmptyMessage = data.ErrorMessage ?? "暂无排期数据";
                SetState(ComponentState.NoSchedule);
                break;

            case ComponentState.NetworkError:
                SetState(ComponentState.NetworkError, data.ErrorMessage ?? "网络错误");
                break;

            default:
                SetState(ComponentState.Loading);
                break;
        }
    }

    private void SetState(ComponentState state, string? message = null)
    {
        IsLoading = state == ComponentState.Loading;
        IsNormal = state == ComponentState.Normal;
        IsEmpty = state == ComponentState.NoSchedule;
        IsError = state == ComponentState.NetworkError;

        if (!string.IsNullOrWhiteSpace(message))
        {
            if (state == ComponentState.NetworkError)
            {
                ErrorMessage = message;
            }
            else if (state == ComponentState.NoSchedule)
            {
                EmptyMessage = message;
            }
        }
    }

    [RelayCommand]
    private async Task RetryAsync()
    {
        _scheduleService.ClearCache();
        await LoadAsync();
    }

    public async Task RefreshAsync()
    {
        await LoadAsync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _refreshTimer?.Stop();
        _loadCts?.Cancel();
        _settingsService.SettingsChanged -= OnSettingsChanged;
    }
}
