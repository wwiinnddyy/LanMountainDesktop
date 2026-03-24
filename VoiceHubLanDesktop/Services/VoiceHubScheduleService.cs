using VoiceHubLanDesktop.Models;

namespace VoiceHubLanDesktop.Services;

/// <summary>
/// 排期管理服务
/// </summary>
public sealed class VoiceHubScheduleService
{
    private readonly VoiceHubApiService _apiService;
    private readonly VoiceHubSettingsService _settingsService;
    private IReadOnlyList<SongItem> _cachedSchedule = [];
    private DateTime _cacheTime = DateTime.MinValue;
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);

    public event EventHandler<ScheduleUpdatedEventArgs>? ScheduleUpdated;

    public VoiceHubScheduleService(VoiceHubApiService apiService, VoiceHubSettingsService settingsService)
    {
        _apiService = apiService;
        _settingsService = settingsService;
    }

    /// <summary>
    /// 获取今日排期
    /// </summary>
    public async Task<DisplayData> GetTodayScheduleAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.GetSettings();

        // 检查缓存
        if (_cachedSchedule.Count > 0 && DateTime.Now - _cacheTime < _cacheExpiry)
        {
            return BuildDisplayData(_cachedSchedule);
        }

        // 从 API 获取
        var result = await _apiService.GetPublicScheduleAsync(settings.ApiUrl, cancellationToken);

        if (!result.IsSuccess)
        {
            return new DisplayData
            {
                State = ComponentState.NetworkError,
                ErrorMessage = result.ErrorMessage ?? "获取排期失败"
            };
        }

        var items = result.Data ?? [];

        // 更新缓存
        _cachedSchedule = items;
        _cacheTime = DateTime.Now;

        return BuildDisplayData(items);
    }

    /// <summary>
    /// 强制刷新
    /// </summary>
    public async Task<DisplayData> RefreshAsync(CancellationToken cancellationToken = default)
    {
        _cachedSchedule = [];
        _cacheTime = DateTime.MinValue;
        return await GetTodayScheduleAsync(cancellationToken);
    }

    /// <summary>
    /// 清除缓存
    /// </summary>
    public void ClearCache()
    {
        _cachedSchedule = [];
        _cacheTime = DateTime.MinValue;
    }

    private DisplayData BuildDisplayData(IReadOnlyList<SongItem> items)
    {
        if (items.Count == 0)
        {
            return new DisplayData
            {
                State = ComponentState.NoSchedule,
                ErrorMessage = "暂无排期数据"
            };
        }

        // 过滤有效日期
        var validItems = items.Where(s => s.GetPlayDate() != DateTime.MinValue).ToList();

        if (validItems.Count == 0)
        {
            return new DisplayData
            {
                State = ComponentState.NoSchedule,
                ErrorMessage = "暂无有效排期数据"
            };
        }

        // 找到今天或最近未来的排期
        var today = DateTime.Today;
        var todaySchedule = validItems
            .Where(s => s.GetPlayDate() == today)
            .OrderBy(s => s.Sequence)
            .ToList();

        List<SongItem> displayItems;
        DateTime actualDate;

        if (todaySchedule.Count > 0)
        {
            displayItems = todaySchedule;
            actualDate = today;
        }
        else
        {
            // 找最近的未来排期
            var futureSchedule = validItems
                .Where(s => s.GetPlayDate() > today)
                .GroupBy(s => s.GetPlayDate())
                .OrderBy(g => g.Key)
                .FirstOrDefault();

            if (futureSchedule != null)
            {
                displayItems = futureSchedule.OrderBy(s => s.Sequence).ToList();
                actualDate = futureSchedule.Key;
            }
            else
            {
                return new DisplayData
                {
                    State = ComponentState.NoSchedule,
                    ErrorMessage = "暂无排期数据"
                };
            }
        }

        // 触发更新事件
        ScheduleUpdated?.Invoke(this, new ScheduleUpdatedEventArgs(displayItems, actualDate));

        return new DisplayData
        {
            State = ComponentState.Normal,
            Songs = displayItems,
            DisplayDate = actualDate
        };
    }
}

/// <summary>
/// 排期更新事件参数
/// </summary>
public sealed class ScheduleUpdatedEventArgs : EventArgs
{
    public IReadOnlyList<SongItem> Songs { get; }
    public DateTime DisplayDate { get; }

    public ScheduleUpdatedEventArgs(IReadOnlyList<SongItem> songs, DateTime displayDate)
    {
        Songs = songs;
        DisplayDate = displayDate;
    }
}
