using System.Text.Json.Serialization;

namespace VoiceHubLanDesktop.Models;

/// <summary>
/// 歌曲信息
/// </summary>
public sealed class Song
{
    /// <summary>
    /// 歌曲标题
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 艺术家/歌手
    /// </summary>
    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;

    /// <summary>
    /// 点歌人
    /// </summary>
    [JsonPropertyName("requester")]
    public string Requester { get; set; } = string.Empty;

    /// <summary>
    /// 投票数/热度
    /// </summary>
    [JsonPropertyName("voteCount")]
    public int VoteCount { get; set; }
}

/// <summary>
/// 排期歌曲项目
/// </summary>
public sealed class SongItem
{
    /// <summary>
    /// 播放日期 (yyyy-MM-dd)
    /// </summary>
    [JsonPropertyName("playDate")]
    public string PlayDate { get; set; } = string.Empty;

    /// <summary>
    /// 播放序号
    /// </summary>
    [JsonPropertyName("sequence")]
    public int Sequence { get; set; }

    /// <summary>
    /// 歌曲信息
    /// </summary>
    [JsonPropertyName("song")]
    public Song Song { get; set; } = new();

    /// <summary>
    /// 获取播放日期
    /// </summary>
    public DateTime GetPlayDate()
    {
        if (string.IsNullOrWhiteSpace(PlayDate))
        {
            return DateTime.MinValue;
        }

        if (DateTime.TryParseExact(PlayDate, "yyyy-MM-dd", null,
            System.Globalization.DateTimeStyles.None, out var result))
        {
            return result;
        }

        return DateTime.MinValue;
    }
}

/// <summary>
/// 组件状态
/// </summary>
public enum ComponentState
{
    /// <summary>
    /// 加载中
    /// </summary>
    Loading,

    /// <summary>
    /// 正常显示
    /// </summary>
    Normal,

    /// <summary>
    /// 网络错误
    /// </summary>
    NetworkError,

    /// <summary>
    /// 暂无排期
    /// </summary>
    NoSchedule
}

/// <summary>
/// 显示数据
/// </summary>
public sealed class DisplayData
{
    public ComponentState State { get; set; }
    public IReadOnlyList<SongItem> Songs { get; set; } = [];
    public DateTime? DisplayDate { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}
