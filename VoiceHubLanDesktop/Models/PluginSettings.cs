namespace VoiceHubLanDesktop.Models;

/// <summary>
/// 插件设置
/// </summary>
public sealed class PluginSettings
{
    /// <summary>
    /// API 地址
    /// </summary>
    public string ApiUrl { get; set; } = "https://voicehub.lao-shui.top/api/songs/public";

    /// <summary>
    /// 是否显示点歌人
    /// </summary>
    public bool ShowRequester { get; set; } = true;

    /// <summary>
    /// 是否显示投票数
    /// </summary>
    public bool ShowVoteCount { get; set; } = false;

    /// <summary>
    /// 刷新间隔（分钟）
    /// </summary>
    public int RefreshIntervalMinutes { get; set; } = 60;
}
