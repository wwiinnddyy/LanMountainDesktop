using System.Text.Json.Serialization;

namespace VoiceHubLanDesktop;

/// <summary>
/// 歌曲信息
/// </summary>
public sealed class Song
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;

    [JsonPropertyName("requester")]
    public string Requester { get; set; } = string.Empty;

    [JsonPropertyName("voteCount")]
    public int VoteCount { get; set; }
}

/// <summary>
/// 排期歌曲项目
/// </summary>
public sealed class SongItem
{
    [JsonPropertyName("playDate")]
    public string PlayDate { get; set; } = string.Empty;

    [JsonPropertyName("sequence")]
    public int Sequence { get; set; }

    [JsonPropertyName("song")]
    public Song Song { get; set; } = new();

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
