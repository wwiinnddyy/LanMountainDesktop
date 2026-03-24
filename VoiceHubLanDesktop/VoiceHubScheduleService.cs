using LanMountainDesktop.PluginSdk;

namespace VoiceHubLanDesktop;

/// <summary>
/// 排期管理服务
/// </summary>
public sealed class VoiceHubScheduleService
{
    private readonly VoiceHubApiService _apiService;
    private readonly IPluginSettingsService _settingsService;
    private IReadOnlyList<SongItem> _cachedSchedule = [];
    private DateTime _cacheTime = DateTime.MinValue;
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);

    private const string SettingsSectionId = "voicehub-settings";

    public VoiceHubScheduleService(VoiceHubApiService apiService, IPluginSettingsService settingsService)
    {
        _apiService = apiService;
        _settingsService = settingsService;
    }

    public async Task<DisplayData> GetTodayScheduleAsync(CancellationToken cancellationToken = default)
    {
        var apiUrl = GetApiUrl();

        if (_cachedSchedule.Count > 0 && DateTime.Now - _cacheTime < _cacheExpiry)
        {
            return BuildDisplayData(_cachedSchedule);
        }

        var result = await _apiService.GetPublicScheduleAsync(apiUrl, cancellationToken);

        if (!result.IsSuccess)
        {
            return new DisplayData
            {
                State = ComponentState.NetworkError,
                ErrorMessage = result.ErrorMessage ?? "获取排期失败"
            };
        }

        var items = result.Data ?? [];
        _cachedSchedule = items;
        _cacheTime = DateTime.Now;

        return BuildDisplayData(items);
    }

    public void ClearCache()
    {
        _cachedSchedule = [];
        _cacheTime = DateTime.MinValue;
    }

    private string GetApiUrl()
    {
        try
        {
            var apiUrl = _settingsService.GetValue<string>(SettingsScope.Plugin, "apiUrl", sectionId: SettingsSectionId);
            return string.IsNullOrWhiteSpace(apiUrl) 
                ? "https://voicehub.lao-shui.top/api/songs/public" 
                : apiUrl;
        }
        catch
        {
            return "https://voicehub.lao-shui.top/api/songs/public";
        }
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

        var validItems = items.Where(s => s.GetPlayDate() != DateTime.MinValue).ToList();

        if (validItems.Count == 0)
        {
            return new DisplayData
            {
                State = ComponentState.NoSchedule,
                ErrorMessage = "暂无有效排期数据"
            };
        }

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

        return new DisplayData
        {
            State = ComponentState.Normal,
            Songs = displayItems,
            DisplayDate = actualDate
        };
    }
}

public enum ComponentState
{
    Loading,
    Normal,
    NetworkError,
    NoSchedule
}

public sealed class DisplayData
{
    public ComponentState State { get; set; }
    public IReadOnlyList<SongItem> Songs { get; set; } = [];
    public DateTime? DisplayDate { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}
