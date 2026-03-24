using LanMountainDesktop.PluginSdk;
using VoiceHubLanDesktop.Models;

namespace VoiceHubLanDesktop.Services;

/// <summary>
/// 插件设置服务
/// </summary>
public sealed class VoiceHubSettingsService
{
    private readonly IPluginSettingsService _settingsService;
    private const string SettingsSectionId = "voicehub-settings";
    private PluginSettings? _cachedSettings;

    public event EventHandler<PluginSettings>? SettingsChanged;

    public VoiceHubSettingsService(IPluginSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// 获取设置
    /// </summary>
    public PluginSettings GetSettings()
    {
        if (_cachedSettings != null)
        {
            return _cachedSettings;
        }

        var settings = new PluginSettings();

        try
        {
            var apiUrl = _settingsService.GetValue<string>(SettingsScope.Plugin, "apiUrl", SettingsSectionId);
            if (!string.IsNullOrWhiteSpace(apiUrl))
            {
                settings.ApiUrl = apiUrl;
            }

            var showRequester = _settingsService.GetValue<bool?>(SettingsScope.Plugin, "showRequester", SettingsSectionId);
            if (showRequester.HasValue)
            {
                settings.ShowRequester = showRequester.Value;
            }

            var showVoteCount = _settingsService.GetValue<bool?>(SettingsScope.Plugin, "showVoteCount", SettingsSectionId);
            if (showVoteCount.HasValue)
            {
                settings.ShowVoteCount = showVoteCount.Value;
            }

            var refreshInterval = _settingsService.GetValue<string>(SettingsScope.Plugin, "refreshInterval", SettingsSectionId);
            if (!string.IsNullOrWhiteSpace(refreshInterval) && int.TryParse(refreshInterval, out var minutes))
            {
                settings.RefreshIntervalMinutes = minutes;
            }
        }
        catch
        {
            // 使用默认值
        }

        _cachedSettings = settings;
        return settings;
    }

    /// <summary>
    /// 保存设置
    /// </summary>
    public void SaveSettings(PluginSettings settings)
    {
        try
        {
            _settingsService.SetValue(SettingsScope.Plugin, "apiUrl", settings.ApiUrl, sectionId: SettingsSectionId);
            _settingsService.SetValue(SettingsScope.Plugin, "showRequester", settings.ShowRequester, sectionId: SettingsSectionId);
            _settingsService.SetValue(SettingsScope.Plugin, "showVoteCount", settings.ShowVoteCount, sectionId: SettingsSectionId);
            _settingsService.SetValue(SettingsScope.Plugin, "refreshInterval", settings.RefreshIntervalMinutes.ToString(), sectionId: SettingsSectionId);

            _cachedSettings = settings;
            SettingsChanged?.Invoke(this, settings);
        }
        catch
        {
            // 忽略保存错误
        }
    }

    /// <summary>
    /// 清除缓存
    /// </summary>
    public void ClearCache()
    {
        _cachedSettings = null;
    }
}
